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
            timing.EmDashPause);
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
                    audioSegments.Add(spoken);

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
}
