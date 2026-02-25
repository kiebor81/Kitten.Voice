namespace Kitten.Voice.Synthesis;

/// <summary>
/// Timing settings used by synthesis orchestration helpers.
/// </summary>
internal readonly record struct SynthesisTimingOptions(
    TimeSpan NewlinePause,
    TimeSpan EllipsisPause,
    TimeSpan EmDashPause,
    TimeSpan CommaPause,
    TimeSpan SemicolonPause,
    TimeSpan ColonPause,
    TimeSpan PeriodPause,
    TimeSpan QuestionPause,
    TimeSpan ExclamationPause,
    TimeSpan MaxAggregatedTextPause,
    TimeSpan ChunkJoinPause)
{
    internal static SynthesisTimingOptions Default { get; } = new(
        NewlinePause: TimeSpan.FromMilliseconds(220),
        EllipsisPause: TimeSpan.FromMilliseconds(280),
        EmDashPause: TimeSpan.FromMilliseconds(170),
        CommaPause: TimeSpan.FromMilliseconds(90),
        SemicolonPause: TimeSpan.FromMilliseconds(140),
        ColonPause: TimeSpan.FromMilliseconds(140),
        PeriodPause: TimeSpan.FromMilliseconds(220),
        QuestionPause: TimeSpan.FromMilliseconds(240),
        ExclamationPause: TimeSpan.FromMilliseconds(220),
        MaxAggregatedTextPause: TimeSpan.FromMilliseconds(1200),
        ChunkJoinPause: TimeSpan.FromMilliseconds(40));
}
