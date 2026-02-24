namespace Kitten.Voice.Synthesis;

/// <summary>
/// Timing settings used by synthesis orchestration helpers.
/// </summary>
internal readonly record struct SynthesisTimingOptions(
    TimeSpan NewlinePause,
    TimeSpan EllipsisPause,
    TimeSpan EmDashPause,
    TimeSpan MaxAggregatedTextPause,
    TimeSpan ChunkJoinPause)
{
    internal static SynthesisTimingOptions Default { get; } = new(
        NewlinePause: TimeSpan.FromMilliseconds(220),
        EllipsisPause: TimeSpan.FromMilliseconds(280),
        EmDashPause: TimeSpan.FromMilliseconds(170),
        MaxAggregatedTextPause: TimeSpan.FromMilliseconds(1200),
        ChunkJoinPause: TimeSpan.FromMilliseconds(40));
}
