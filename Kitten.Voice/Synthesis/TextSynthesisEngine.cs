using Kitten.Voice.Audio;
using Kitten.Voice.TextProcessing;

namespace Kitten.Voice.Synthesis;

/// <summary>
/// Provides methods for synthesizing text with special handling for pauses and chunking to fit token limits.
/// </summary>
internal static class TextSynthesisEngine
{
    /// <summary>
    /// Defines a delegate for synthesizing a segment of text with optional style overrides and blending.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="styleRowOverride"></param>
    /// <param name="styleBlend"></param>
    /// <returns></returns>
    internal delegate float[] SegmentSynthesizer(string text, int? styleRowOverride, float styleBlend);

    /// <summary>
    /// Synthesizes the given text while inserting pauses based on the presence of newlines, ellipses, and em dashes.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="styleRowOverride"></param>
    /// <param name="styleBlend"></param>
    /// <param name="sampleRate"></param>
    /// <param name="newlinePause"></param>
    /// <param name="ellipsisPause"></param>
    /// <param name="emDashPause"></param>
    /// <param name="maxAggregatedTextPause"></param>
    /// <param name="synthesizeSegment"></param>
    /// <returns></returns>
    internal static float[] SynthesizeWithTextPauses(
        string text,
        int? styleRowOverride,
        float styleBlend,
        int sampleRate,
        SynthesisTimingOptions timing,
        SegmentSynthesizer synthesizeSegment)
    {
        List<PlainTextPauseSegment> segments = PlainTextPauseParser.Split(
            text,
            timing.NewlinePause,
            timing.EllipsisPause,
            timing.EmDashPause,
            timing.CommaPause,
            timing.SemicolonPause,
            timing.ColonPause,
            timing.PeriodPause,
            timing.QuestionPause,
            timing.ExclamationPause);
        var audioSegments = new List<float[]>();
        TimeSpan pendingPause = TimeSpan.Zero;

        foreach (PlainTextPauseSegment segmentInfo in segments)
        {
            string segment = segmentInfo.Text.Trim();
            if (segment.Length > 0)
            {
                if (audioSegments.Count > 0 && pendingPause > TimeSpan.Zero)
                    audioSegments.Add(WaveformProcessor.GenerateSilence(sampleRate, pendingPause));

                float[] spoken = synthesizeSegment(segment, styleRowOverride, styleBlend);
                if (spoken.Length > 0)
                {
                    ApplyPunctuationInflection(spoken, segmentInfo.InflectionIntent, sampleRate, timing);
                    audioSegments.Add(spoken);
                }

                pendingPause = TimeSpan.Zero;
            }

            TimeSpan pauseAfter = segmentInfo.PauseAfter;
            if (pauseAfter > TimeSpan.Zero)
            {
                pendingPause = TimeSpan.FromMilliseconds(
                    Math.Min((pendingPause + pauseAfter).TotalMilliseconds, timing.MaxAggregatedTextPause.TotalMilliseconds));
            }
        }

        return audioSegments.Count > 0
            ? WaveformProcessor.Concatenate([.. audioSegments])
            : [];
    }

    /// <summary>
    /// Synthesizes the given text in chunks that fit within the specified token limit, inserting pauses between chunks as needed.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="maxInputTokenCount"></param>
    /// <param name="sampleRate"></param>
    /// <param name="chunkJoinPause"></param>
    /// <param name="getTokenCount"></param>
    /// <param name="synthesizeSegment"></param>
    /// <param name="styleRowOverride"></param>
    /// <param name="styleBlend"></param>
    /// <returns></returns>
    internal static float[] SynthesizeChunked(
        string text,
        int maxInputTokenCount,
        int sampleRate,
        SynthesisTimingOptions timing,
        Func<string, int> getTokenCount,
        SegmentSynthesizer synthesizeSegment,
        int? styleRowOverride,
        float styleBlend)
    {
        List<string> chunks = TextChunker.SplitByTokenLimit(text, maxInputTokenCount, getTokenCount);
        if (chunks.Count == 0)
            return [];

        var audioChunks = new List<float[]>(chunks.Count + 4);
        for (int i = 0; i < chunks.Count; i++)
        {
            float[] chunkAudio = synthesizeSegment(chunks[i], styleRowOverride, styleBlend);
            if (chunkAudio.Length == 0)
                continue;

            if (audioChunks.Count > 0)
                audioChunks.Add(WaveformProcessor.GenerateSilence(sampleRate, timing.ChunkJoinPause));

            audioChunks.Add(chunkAudio);
        }

        return audioChunks.Count > 0
            ? WaveformProcessor.Concatenate([.. audioChunks])
            : [];
    }

    private static void ApplyPunctuationInflection(
        float[] samples,
        PlainTextInflectionIntent intent,
        int sampleRate,
        SynthesisTimingOptions timing)
    {
        if (!timing.EnablePunctuationInflection || intent is PlainTextInflectionIntent.None or PlainTextInflectionIntent.Statement)
            return;

        float semitones = intent switch
        {
            PlainTextInflectionIntent.Question => timing.QuestionTailPitchSemitones,
            PlainTextInflectionIntent.Exclamation => timing.ExclamationTailPitchSemitones,
            _ => 0f,
        };

        float volumeBoost = intent == PlainTextInflectionIntent.Exclamation
            ? timing.ExclamationTailVolumeBoost
            : 0f;

        if (Math.Abs(semitones) < 0.01f && volumeBoost <= 0.001f)
            return;

        int lastActive = FindLastActiveSample(samples);
        if (lastActive <= 1)
            return;

        int targetTail = (int)(sampleRate * timing.PunctuationInflectionTail.TotalSeconds);
        if (targetTail < 32)
            return;

        int endExclusive = lastActive + 1;
        int tailLength = Math.Min(targetTail, endExclusive);
        int start = endExclusive - tailLength;
        if (tailLength < 32 || start < 0)
            return;

        var tail = new float[tailLength];
        Array.Copy(samples, start, tail, 0, tailLength);

        if (Math.Abs(semitones) >= 0.01f)
        {
            float[] shifted = WaveformProcessor.ApplyPitchShift(tail, sampleRate, semitones);
            float[] resampledShifted = ResampleToLength(shifted, tailLength);

            for (int i = 0; i < tailLength; i++)
            {
                float blend = (float)i / (tailLength - 1);
                tail[i] = Lerp(tail[i], resampledShifted[i], blend);
            }
        }

        if (volumeBoost > 0.001f)
        {
            for (int i = 0; i < tailLength; i++)
            {
                float blend = (float)i / (tailLength - 1);
                float gain = 1f + (volumeBoost * blend);
                tail[i] = Math.Clamp(tail[i] * gain, -1f, 1f);
            }
        }

        Array.Copy(tail, 0, samples, start, tailLength);
        WaveformProcessor.ApplyPeakLimiter(samples);
    }

    private static int FindLastActiveSample(float[] samples, float threshold = 0.0005f)
    {
        for (int i = samples.Length - 1; i >= 0; i--)
        {
            if (Math.Abs(samples[i]) > threshold)
                return i;
        }

        return -1;
    }

    private static float[] ResampleToLength(float[] samples, int targetLength)
    {
        if (targetLength <= 0)
            return [];

        if (samples.Length == targetLength)
            return samples;

        if (samples.Length == 0)
            return new float[targetLength];

        if (targetLength == 1)
            return [samples[0]];

        var result = new float[targetLength];
        float scale = (samples.Length - 1f) / (targetLength - 1f);

        for (int i = 0; i < targetLength; i++)
        {
            float source = i * scale;
            int index = (int)source;
            float frac = source - index;

            if (index + 1 < samples.Length)
                result[i] = Lerp(samples[index], samples[index + 1], frac);
            else
                result[i] = samples[^1];
        }

        return result;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
