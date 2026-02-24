using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Kitten.Voice;

/// <summary>
/// Controls how audio is output after synthesis.
/// </summary>
public enum AudioOutput
{
    /// <summary>Stream directly to speakers from memory; no file written.</summary>
    Stream,

    /// <summary>Save to a WAV file, then play from disk.</summary>
    File,

    /// <summary>Save to a WAV file without playing.</summary>
    FileOnly,
}

/// <summary>
/// Configurable TTS interface for KittenTTS ONNX models.
/// </summary>
/// <remarks>
/// Creates a Speaker using assets from the specified directory.
/// </remarks>
public class Speaker(string assetsDir = "assets")
{
    private const int SampleRate = 24000;
    private const int MaxInputTokenCount = 500;
    private static readonly TimeSpan NewlinePause = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan EllipsisPause = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan EmDashPause = TimeSpan.FromMilliseconds(170);
    private static readonly TimeSpan ChunkJoinPause = TimeSpan.FromMilliseconds(40);
    private static readonly HashSet<string> DistortionProneEmotions = new(StringComparer.OrdinalIgnoreCase)
    {
        "excited",
        "fearful",
        "angry",
    };

    private readonly record struct EmotionProfile(float Volume, float Pitch, float Speed);
    private readonly record struct EmotionModifiers(float VolumeMultiplier, float PitchSemitones, float SpeedMultiplier);
    private readonly record struct EmotionStyleSelection(int? Row, float Blend);

    private static readonly Dictionary<string, string> EmotionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joyful"] = "happy",
        ["cheerful"] = "happy",
        ["angry"] = "angry",
        ["mad"] = "angry",
        ["furious"] = "angry",
        ["depressed"] = "sad",
        ["melancholy"] = "sad",
        ["relaxed"] = "calm",
        ["serene"] = "calm",
        ["fear"] = "fearful",
    };

    private static readonly Dictionary<string, EmotionProfile> EmotionProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"] = new(1.00f, 0.00f, 1.00f),
        ["happy"] = new(1.01f, 0.2f, 1.1f),
        ["excited"] = new(1.02f, 0.25f, 1.1f),
        ["sad"] = new(0.90f, -0.25f, 0.90f),
        ["angry"] = new(1.03f, 0.25f, 1.1f),
        ["calm"] = new(0.94f, -0.14f, 0.92f),
        ["fearful"] = new(1.01f, 0.25f, 1.1f),
    };

    private static readonly Dictionary<string, int[]> EmotionStyleRows = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"] = [0],
        ["happy"] = [12, 20, 28],
        ["excited"] = [30, 42, 56],
        ["sad"] = [68, 80, 96],
        ["angry"] = [104, 118, 132],
        ["calm"] = [6, 10, 14],
        ["fearful"] = [144, 160, 176],
    };

    private readonly ModelConfig _config = LoadConfigAndApplyOverrides(assetsDir);
    private readonly KokoroTokenizer _tokenizer = KokoroTokenizer.Load(Path.Combine(assetsDir, "tokenizer.json"));

    public string Voice { get; set; } = "Bella";
    public float Speed { get; set; } = 1.3f;
    /// <summary>
    /// Global multiplier for SSML emotion intensity (1.0 = unchanged).
    /// 0 disables emotion effects, values above 1 increase expressiveness.
    /// </summary>
    public float Expressiveness { get; set; } = 1.0f;
    public AudioOutput Output { get; set; } = AudioOutput.Stream;
    public string OutputPath { get; set; } = "output.wav";

    /// <summary>
    /// Converts text or SSML to speech and outputs according to <see cref="Output"/> mode.
    /// </summary>
    /// <remarks>
    /// If the input starts with &lt;, it is parsed as SSML.
    /// Supported tags: &lt;break&gt;, &lt;prosody&gt;, &lt;emphasis&gt;, &lt;voice&gt;, &lt;say-as&gt;, &lt;emotion&gt;, &lt;express-as&gt;.
    /// Plain text is synthesized directly with current settings.
    /// </remarks>
    public void Say(string text)
    {
        float[] audio = SsmlParser.IsSsml(text)
            ? SynthesizeSsml(text)
            : Synthesize(text);

        if (audio.Length == 0) return;

        switch (Output)
        {
            case AudioOutput.Stream:
                AudioHelper.PlayFromMemory(audio, SampleRate);
                break;

            case AudioOutput.File:
                AudioHelper.SaveNormalizedWav(OutputPath, audio, SampleRate);
                AudioHelper.Play(OutputPath);
                break;

            case AudioOutput.FileOnly:
                AudioHelper.SaveNormalizedWav(OutputPath, audio, SampleRate);
                break;
        }
    }

    /// <summary>
    /// Parses SSML and synthesizes each segment with its own settings,
    /// then stitches the results into a single waveform.
    /// </summary>
    public float[] SynthesizeSsml(string ssml)
    {
        var segments = SsmlParser.Parse(ssml);
        var audioSegments = new List<float[]>();

        foreach (var segment in segments)
        {
            if (segment.IsBreak)
            {
                if (segment.BreakBefore > TimeSpan.Zero)
                    audioSegments.Add(WaveformProcessor.GenerateSilence(SampleRate, segment.BreakBefore));
                continue;
            }

            if (segment.BreakBefore > TimeSpan.Zero)
                audioSegments.Add(WaveformProcessor.GenerateSilence(SampleRate, segment.BreakBefore));

            // Synthesize with per-segment speed and optional voice override
            string savedVoice = Voice;
            float savedSpeed = Speed;
            try
            {
                if (segment.Voice != null)
                    Voice = segment.Voice;

                bool distortionProne = IsDistortionProneEmotion(segment.Emotion);
                float effectiveEmotionIntensity = ResolveEffectiveEmotionIntensity(segment.EmotionIntensity);
                EmotionModifiers emotion = ResolveEmotionModifiers(segment.Emotion, effectiveEmotionIntensity);
                Speed = savedSpeed * segment.Speed * emotion.SpeedMultiplier;

                EmotionStyleSelection style = ResolveEmotionStyleSelection(segment.Emotion, effectiveEmotionIntensity);
                float[] audio = Synthesize(segment.Text, style.Row, style.Blend);

                if (audio.Length > 0)
                {
                    float effectiveVolume = segment.Volume * emotion.VolumeMultiplier;
                    float emotionPitch = distortionProne ? (emotion.PitchSemitones * 0.12f) : emotion.PitchSemitones;
                    float effectivePitch = segment.PitchShift + emotionPitch;
                    float pitchShiftThreshold = distortionProne ? 0.30f : 0.10f;
                    float maxVolume = distortionProne ? 1.08f : 1.20f;
                    effectiveVolume = Math.Clamp(effectiveVolume, 0f, maxVolume);
                    WaveformProcessor.ApplyVolume(audio, effectiveVolume);

                    if (Math.Abs(effectivePitch) > pitchShiftThreshold)
                        audio = WaveformProcessor.ApplyPitchShift(audio, SampleRate, effectivePitch);

                    if (distortionProne)
                        WaveformProcessor.ApplySoftClip(audio, 1.08f);

                    WaveformProcessor.ApplyPeakLimiter(audio);

                    audioSegments.Add(audio);
                }
            }
            finally
            {
                Voice = savedVoice;
                Speed = savedSpeed;
            }
        }

        return audioSegments.Count > 0
            ? WaveformProcessor.Concatenate([.. audioSegments])
            : [];
    }

    /// <summary>
    /// Synthesizes speech and returns the processed audio samples without playing.
    /// </summary>
    public float[] Synthesize(string text, int? styleRowOverride = null, float styleBlend = 1.0f)
    {
        if (ContainsTextPauseCue(text))
            return SynthesizeWithTextPauses(text, styleRowOverride, styleBlend);

        string normalized = EnsureTrailingPunctuation(text);
        long[] tokenIds = _tokenizer.Process(normalized);
        if (tokenIds.Length > MaxInputTokenCount)
            return SynthesizeChunked(text, styleRowOverride, styleBlend);

        if (tokenIds.Max() >= _tokenizer.VocabSize)
            throw new InvalidOperationException(
                $"Token ID {tokenIds.Max()} exceeds vocab size {_tokenizer.VocabSize}");

        string resolvedVoice = _config.ResolveVoice(Voice);
        int baseStyleRow = Math.Max(1, tokenIds.Length - 1);
        float[] styleVector = styleRowOverride.HasValue
            ? VoiceStore.LoadBlendedStyleVector(_config.VoicesPath, resolvedVoice, styleRowOverride.Value, styleBlend, baseStyleRow)
            : VoiceStore.LoadStyleVectorForTokenCount(_config.VoicesPath, resolvedVoice, tokenIds.Length);
        float[] audioData = RunInference(tokenIds, styleVector);

        if (audioData.Length == 0 || audioData.Max(Math.Abs) < 1e-6f)
            return [];

        return AudioHelper.ProcessAudio(audioData, SampleRate);
    }

    private float[] SynthesizeWithTextPauses(string text, int? styleRowOverride, float styleBlend)
    {
        List<(string Text, TimeSpan PauseAfter)> segments = SplitByPauseCues(text);
        var audioSegments = new List<float[]>();
        TimeSpan pendingPause = TimeSpan.Zero;

        foreach ((string rawText, TimeSpan pauseAfter) in segments)
        {
            string segment = rawText.Trim();
            if (segment.Length > 0)
            {
                if (audioSegments.Count > 0 && pendingPause > TimeSpan.Zero)
                    audioSegments.Add(WaveformProcessor.GenerateSilence(SampleRate, pendingPause));

                float[] spoken = Synthesize(segment, styleRowOverride, styleBlend);
                if (spoken.Length > 0)
                    audioSegments.Add(spoken);

                pendingPause = TimeSpan.Zero;
            }

            if (pauseAfter > TimeSpan.Zero)
                pendingPause = TimeSpan.FromMilliseconds(
                    Math.Min((pendingPause + pauseAfter).TotalMilliseconds, 1200));
        }

        return audioSegments.Count > 0
            ? WaveformProcessor.Concatenate([.. audioSegments])
            : [];
    }

    private float[] SynthesizeChunked(string text, int? styleRowOverride, float styleBlend)
    {
        List<string> chunks = SplitTextByTokenLimit(text, MaxInputTokenCount);
        if (chunks.Count == 0)
            return [];

        var audioChunks = new List<float[]>(chunks.Count + 4);
        for (int i = 0; i < chunks.Count; i++)
        {
            float[] chunkAudio = Synthesize(chunks[i], styleRowOverride, styleBlend);
            if (chunkAudio.Length == 0)
                continue;

            if (audioChunks.Count > 0)
                audioChunks.Add(WaveformProcessor.GenerateSilence(SampleRate, ChunkJoinPause));

            audioChunks.Add(chunkAudio);
        }

        return audioChunks.Count > 0
            ? WaveformProcessor.Concatenate([.. audioChunks])
            : [];
    }

    private List<string> SplitTextByTokenLimit(string text, int maxTokenCount)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return [];

        string[] units = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string unit in units)
        {
            string candidate = current.Length == 0 ? unit : $"{current} {unit}";
            if (GetTokenCount(candidate) <= maxTokenCount)
            {
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(unit);
                continue;
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (GetTokenCount(unit) <= maxTokenCount)
            {
                current.Append(unit);
                continue;
            }

            chunks.AddRange(SplitTokenByCharacterLimit(unit, maxTokenCount));
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    private List<string> SplitTokenByCharacterLimit(string token, int maxTokenCount)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (char c in token)
        {
            string candidate = current.Length == 0 ? c.ToString() : $"{current}{c}";
            if (GetTokenCount(candidate) <= maxTokenCount)
            {
                current.Append(c);
                continue;
            }

            if (current.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Input token '{token}' cannot be split to satisfy model token limit {maxTokenCount}.");
            }

            pieces.Add(current.ToString());
            current.Clear();
            current.Append(c);
        }

        if (current.Length > 0)
            pieces.Add(current.ToString());

        return pieces;
    }

    private int GetTokenCount(string text)
    {
        string normalized = EnsureTrailingPunctuation(text);
        return _tokenizer.Process(normalized).Length;
    }

    private static bool ContainsTextPauseCue(string text)
    {
        return text.IndexOfAny(['\r', '\n', '…', '—']) >= 0
            || text.Contains("...", StringComparison.Ordinal);
    }

    private static List<(string Text, TimeSpan PauseAfter)> SplitByPauseCues(string text)
    {
        var segments = new List<(string Text, TimeSpan PauseAfter)>();
        var current = new System.Text.StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '—')
            {
                int dashes = ConsumeRepeatedChar(text, ref i, '—');
                segments.Add((current.ToString(), ScalePauseByCount(EmDashPause, dashes)));
                current.Clear();
                continue;
            }

            if (c == '…')
            {
                int ellipses = ConsumeRepeatedChar(text, ref i, '…');
                segments.Add((current.ToString(), ScalePauseByCount(EllipsisPause, ellipses)));
                current.Clear();
                continue;
            }

            if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
            {
                int dotCount = ConsumeRepeatedChar(text, ref i, '.');
                int ellipsisCount = Math.Max(1, dotCount / 3);
                segments.Add((current.ToString(), ScalePauseByCount(EllipsisPause, ellipsisCount)));
                current.Clear();

                int remainder = dotCount % 3;
                for (int r = 0; r < remainder; r++)
                    current.Append('.');

                continue;
            }

            if (c is '\r' or '\n')
            {
                int breaks = 0;
                while (i < text.Length && text[i] is '\r' or '\n')
                {
                    if (text[i] == '\r')
                    {
                        breaks++;
                        if (i + 1 < text.Length && text[i + 1] == '\n')
                            i++;
                    }
                    else
                    {
                        breaks++;
                    }

                    i++;
                }

                segments.Add((current.ToString(), ScalePauseByCount(NewlinePause, breaks)));
                current.Clear();
                continue;
            }

            current.Append(c);
            i++;
        }

        segments.Add((current.ToString(), TimeSpan.Zero));
        return segments;
    }

    private static int ConsumeRepeatedChar(string text, ref int index, char token)
    {
        int count = 0;
        while (index < text.Length && text[index] == token)
        {
            count++;
            index++;
        }

        return count;
    }

    private static TimeSpan ScalePauseByCount(TimeSpan basePause, int count)
    {
        int normalized = Math.Clamp(count, 1, 4);
        return TimeSpan.FromMilliseconds(basePause.TotalMilliseconds * normalized);
    }

    private float[] RunInference(long[] tokenIds, float[] styleVector)
    {
        using var session = new InferenceSession(_config.ModelPath);
        var inputs = BuildInputs(tokenIds, styleVector, Speed);
        using var results = session.Run(inputs);
        return [.. results.First(r => r.Name == "waveform").AsEnumerable<float>()];
    }

    private static string EnsureTrailingPunctuation(string text)
    {
        string trimmed = text.TrimEnd();
        if (trimmed.Length > 0 && !".!?;:,".Contains(trimmed[^1]))
            trimmed += ".";
        return trimmed;
    }

    private float ResolveEffectiveEmotionIntensity(float segmentIntensity)
    {
        float scalar = Math.Max(0f, Expressiveness);
        return Math.Clamp(segmentIntensity * scalar, 0f, 2.5f);
    }

    private static string CanonicalizeEmotion(string emotion)
    {
        string normalized = emotion.Trim().ToLowerInvariant();
        return EmotionAliases.TryGetValue(normalized, out string? canonical)
            ? canonical
            : normalized;
    }

    private static bool IsDistortionProneEmotion(string? emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return false;

        return DistortionProneEmotions.Contains(CanonicalizeEmotion(emotion));
    }

    private static EmotionModifiers ResolveEmotionModifiers(string? emotion, float intensity)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return new EmotionModifiers(1.0f, 0.0f, 1.0f);

        float normalizedIntensity = Math.Clamp(intensity, 0f, 2.0f);
        if (normalizedIntensity <= 0.001f)
            return new EmotionModifiers(1.0f, 0.0f, 1.0f);

        string canonical = CanonicalizeEmotion(emotion);
        if (!EmotionProfiles.TryGetValue(canonical, out EmotionProfile profile))
            profile = EmotionProfiles["neutral"];

        float pitch = Math.Clamp(profile.Pitch * normalizedIntensity, -0.75f, 0.75f);
        return new EmotionModifiers(
            VolumeMultiplier: 1f + ((profile.Volume - 1f) * normalizedIntensity),
            PitchSemitones: pitch,
            SpeedMultiplier: 1f + ((profile.Speed - 1f) * normalizedIntensity));
    }

    private static EmotionStyleSelection ResolveEmotionStyleSelection(string? emotion, float intensity)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return new EmotionStyleSelection(null, 0f);

        float normalizedIntensity = Math.Clamp(intensity, 0f, 2.0f);
        if (normalizedIntensity <= 0.001f)
            return new EmotionStyleSelection(null, 0f);

        string canonical = CanonicalizeEmotion(emotion);
        if (!EmotionStyleRows.TryGetValue(canonical, out int[]? rows) || rows.Length == 0)
            return new EmotionStyleSelection(null, 0f);

        int bucket = normalizedIntensity < 0.9f
            ? 0
            : normalizedIntensity < 1.25f
                ? Math.Min(1, rows.Length - 1)
                : rows.Length - 1;

        float blend = Math.Clamp(0.22f + (normalizedIntensity * 0.20f), 0.22f, 0.62f);
        if (DistortionProneEmotions.Contains(canonical))
        {
            if (bucket == rows.Length - 1 && rows.Length > 1)
                bucket = rows.Length - 2;

            blend = Math.Min(blend, 0.36f);
        }

        return new EmotionStyleSelection(rows[bucket], blend);
    }

    private static List<NamedOnnxValue> BuildInputs(long[] tokenIds, float[] styleVector, float speed)
    {
        var inputTensor = new DenseTensor<long>([1, tokenIds.Length]);
        for (int i = 0; i < tokenIds.Length; i++)
            inputTensor[0, i] = tokenIds[i];

        var styleTensor = new DenseTensor<float>([1, styleVector.Length]);
        for (int i = 0; i < styleVector.Length; i++)
            styleTensor[0, i] = styleVector[i];

        var speedTensor = new DenseTensor<float>([1]);
        speedTensor[0] = speed;

        return
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("style", styleTensor),
            NamedOnnxValue.CreateFromTensor("speed", speedTensor),
        ];
    }

    private static ModelConfig LoadConfigAndApplyOverrides(string assetsDir)
    {
        ModelConfig config = ModelConfig.Load(assetsDir);
        EnglishToIpa.SetOverrides(config.PronunciationOverrides);
        return config;
    }
}
