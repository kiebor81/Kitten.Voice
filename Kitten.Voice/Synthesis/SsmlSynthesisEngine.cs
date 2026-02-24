using Kitten.Voice.Audio;
using Kitten.Voice.Ssml;

namespace Kitten.Voice.Synthesis;

/// <summary>
/// Synthesis engine that processes a list of speech segments with SSML-like effects and concatenates the resulting audio.
/// </summary>
internal static class SsmlSynthesisEngine
{
    /// <summary>
    /// Delegate for synthesizing text with specific settings. This allows the synthesis engine to be flexible and work with different underlying TTS implementations.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="voice"></param>
    /// <param name="speed"></param>
    /// <param name="styleRowOverride"></param>
    /// <param name="styleBlend"></param>
    /// <returns></returns>
    internal delegate float[] TextSynthesizerWithSettings(
        string text,
        string voice,
        float speed,
        int? styleRowOverride,
        float styleBlend);

    /// <summary>
    /// Synthesizes a list of speech segments into a single audio array, applying SSML-like effects such as breaks, pitch shifts, volume adjustments, and emotional expressiveness.
    /// </summary>
    /// <param name="segments">The list of speech segments to synthesize.</param>
    /// <param name="defaultVoice">The default voice to use if a segment does not specify one.</param>
    /// <param name="defaultSpeed">The default speed to use if a segment does not specify one.</param>
    /// <param name="expressiveness">The expressiveness factor to apply to the synthesis.</param>
    /// <param name="sampleRate">The sample rate of the audio.</param>
    /// <param name="synthesizeText">The delegate to use for synthesizing text with specific settings.</param>
    /// <returns>The synthesized audio as a float array.</returns>
    internal static float[] Synthesize(
        IReadOnlyList<SpeechSegment> segments,
        string defaultVoice,
        float defaultSpeed,
        float expressiveness,
        int sampleRate,
        TextSynthesizerWithSettings synthesizeText)
    {
        var audioSegments = new List<float[]>();

        foreach (SpeechSegment segment in segments)
        {
            if (segment.BreakBefore > TimeSpan.Zero)
                audioSegments.Add(WaveformProcessor.GenerateSilence(sampleRate, segment.BreakBefore));

            if (segment.IsBreak)
                continue;

            EmotionEngine.EmotionResolution emotion = EmotionEngine.Resolve(
                segment.Emotion,
                segment.EmotionIntensity,
                expressiveness);
            string effectiveVoice = segment.Voice ?? defaultVoice;
            float effectiveSpeed = defaultSpeed * segment.Speed * emotion.Modifiers.SpeedMultiplier;

            float[] audio = synthesizeText(
                segment.Text,
                effectiveVoice,
                effectiveSpeed,
                emotion.Style.Row,
                emotion.Style.Blend);
            if (audio.Length == 0)
                continue;

            audioSegments.Add(ApplySsmlEffects(audio, segment, emotion, sampleRate));
        }

        return audioSegments.Count > 0
            ? WaveformProcessor.Concatenate([.. audioSegments])
            : [];
    }

    private static float[] ApplySsmlEffects(
        float[] audio,
        SpeechSegment segment,
        EmotionEngine.EmotionResolution emotion,
        int sampleRate)
    {
        float effectiveVolume = segment.Volume * emotion.Modifiers.VolumeMultiplier;
        float emotionPitch = emotion.DistortionProne
            ? (emotion.Modifiers.PitchSemitones * 0.12f)
            : emotion.Modifiers.PitchSemitones;
        float effectivePitch = segment.PitchShift + emotionPitch;
        float pitchShiftThreshold = emotion.DistortionProne ? 0.30f : 0.10f;
        float maxVolume = emotion.DistortionProne ? 1.08f : 1.20f;
        effectiveVolume = Math.Clamp(effectiveVolume, 0f, maxVolume);
        WaveformProcessor.ApplyVolume(audio, effectiveVolume);

        if (Math.Abs(effectivePitch) > pitchShiftThreshold)
            audio = WaveformProcessor.ApplyPitchShift(audio, sampleRate, effectivePitch);

        if (emotion.DistortionProne)
            WaveformProcessor.ApplySoftClip(audio, 1.08f);

        WaveformProcessor.ApplyPeakLimiter(audio);
        return audio;
    }
}
