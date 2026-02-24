using Kitten.Voice.Audio;
using Kitten.Voice.Configuration;
using Kitten.Voice.Ssml;
using Kitten.Voice.Synthesis;
using Kitten.Voice.TextProcessing;
using Kitten.Voice.Tokenization;
using Kitten.Voice.Embeddings;

namespace Kitten.Voice;

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
    private readonly ModelConfig _config = LoadConfigAndApplyOverrides(assetsDir);
    private readonly KokoroTokenizer _tokenizer = KokoroTokenizer.Load(Path.Combine(assetsDir, "tokenizer.json"));

    /// <summary>
    /// Name of the voice to use for synthesis. Must correspond to a subdirectory in the VoicesPath specified in the model config, or a key in the voice resolution dictionary if used.
    /// </summary>
    public string Voice { get; set; } = "Bella";
    /// <summary>
    /// Global speed multiplier for synthesis (1.0 = unchanged). Values above 1 increase speed, below 1 decrease speed.
    /// </summary>
    public float Speed { get; set; } = 1.3f;
    /// <summary>
    /// Global multiplier for SSML emotion intensity (1.0 = unchanged).
    /// 0 disables emotion effects, values above 1 increase expressiveness.
    /// </summary>
    public float Expressiveness { get; set; } = 1.0f;
    /// <summary>
    /// Timing settings for synthesis orchestration, used when processing SSML or long text with automatic chunking.
    /// </summary>
    internal SynthesisTimingOptions Timing { get; set; } = SynthesisTimingOptions.Default;
    /// <summary>
    /// Output mode for synthesized audio. Stream plays immediately, File saves to disk and plays, and FileOnly saves without playing.
    /// </summary>
    public AudioOutput Output { get; set; } = AudioOutput.Stream;
    /// <summary>
    /// File path for saving synthesized audio when Output mode is set to File. Ignored for other modes.
    /// </summary>
    public string OutputPath { get; set; } = "output.wav";

    /// <summary>
    /// Gets voice names configured in the assets directory.
    /// </summary>
    public static string[] GetAvailableVoices(string assetsDir)
    {
        ModelConfig config = ModelConfig.Load(assetsDir);
        return [.. config.VoiceAliases.Keys.OrderBy(v => v)];
    }

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

        if (audio.Length == 0)
            return;

        AudioOutputDispatcher.Dispatch(Output, OutputPath, audio, SampleRate);
    }

    /// <summary>
    /// Parses SSML and synthesizes each segment with its own settings,
    /// then stitches the results into a single waveform.
    /// </summary>
    public float[] SynthesizeSsml(string ssml)
    {
        IReadOnlyList<SpeechSegment> segments = SsmlParser.Parse(ssml);
        return SsmlSynthesisEngine.Synthesize(
            segments,
            Voice,
            Speed,
            Expressiveness,
            SampleRate,
            SynthesizeWithSettings);
    }

    /// <summary>
    /// Synthesizes speech and returns the processed audio samples without playing.
    /// </summary>
    public float[] Synthesize(string text, int? styleRowOverride = null, float styleBlend = 1.0f)
    {
        return SynthesizeCore(text, Voice, Speed, styleRowOverride, styleBlend);
    }

    private float[] SynthesizeWithSettings(
        string text,
        string voice,
        float speed,
        int? styleRowOverride,
        float styleBlend)
    {
        return SynthesizeCore(text, voice, speed, styleRowOverride, styleBlend);
    }

    private float[] SynthesizeCore(
        string text,
        string voice,
        float speed,
        int? styleRowOverride,
        float styleBlend)
    {
        if (PlainTextPauseParser.ContainsPauseCue(text))
        {
            return SynthesizeWithTextPauses(
                text,
                voice,
                speed,
                styleRowOverride,
                styleBlend);
        }

        string normalized = EnsureTrailingPunctuation(text);
        long[] tokenIds = _tokenizer.Process(normalized);
        if (tokenIds.Length > MaxInputTokenCount)
        {
            return SynthesizeChunked(
                text,
                voice,
                speed,
                styleRowOverride,
                styleBlend);
        }

        if (tokenIds.Max() >= _tokenizer.VocabSize)
            throw new InvalidOperationException(
                $"Token ID {tokenIds.Max()} exceeds vocab size {_tokenizer.VocabSize}");

        string resolvedVoice = _config.ResolveVoice(voice);
        int baseStyleRow = Math.Max(1, tokenIds.Length - 1);
        float[] styleVector = styleRowOverride.HasValue
            ? VoiceStore.LoadBlendedStyleVector(_config.VoicesPath, resolvedVoice, styleRowOverride.Value, styleBlend, baseStyleRow)
            : VoiceStore.LoadStyleVectorForTokenCount(_config.VoicesPath, resolvedVoice, tokenIds.Length);
        float[] audioData = OnnxInferenceEngine.Run(_config.ModelPath, tokenIds, styleVector, speed);

        if (audioData.Length == 0 || audioData.Max(Math.Abs) < 1e-6f)
            return [];

        return AudioHelper.ProcessAudio(audioData, SampleRate);
    }

    private float[] SynthesizeWithTextPauses(
        string text,
        string voice,
        float speed,
        int? styleRowOverride,
        float styleBlend)
    {
        return TextSynthesisEngine.SynthesizeWithTextPauses(
            text,
            styleRowOverride,
            styleBlend,
            SampleRate,
            Timing,
            (segmentText, row, blend) => SynthesizeCore(segmentText, voice, speed, row, blend));
    }

    private float[] SynthesizeChunked(
        string text,
        string voice,
        float speed,
        int? styleRowOverride,
        float styleBlend)
    {
        return TextSynthesisEngine.SynthesizeChunked(
            text,
            MaxInputTokenCount,
            SampleRate,
            Timing,
            GetTokenCount,
            (segmentText, row, blend) => SynthesizeCore(segmentText, voice, speed, row, blend),
            styleRowOverride,
            styleBlend);
    }

    private int GetTokenCount(string text)
    {
        string normalized = EnsureTrailingPunctuation(text);
        return _tokenizer.Process(normalized).Length;
    }

    private static string EnsureTrailingPunctuation(string text)
    {
        string trimmed = text.TrimEnd();
        if (trimmed.Length > 0 && !".!?;:,".Contains(trimmed[^1]))
            trimmed += ".";
        return trimmed;
    }

    private static ModelConfig LoadConfigAndApplyOverrides(string assetsDir)
    {
        ModelConfig config = ModelConfig.Load(assetsDir);
        EnglishToIpa.SetOverrides(config.PronunciationOverrides);
        return config;
    }
}
