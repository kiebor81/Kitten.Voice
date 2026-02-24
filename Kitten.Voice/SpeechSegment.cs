namespace Kitten.Voice;

/// <summary>
/// Represents a segment of text with associated speech properties,
/// produced by parsing SSML markup.
/// </summary>
public class SpeechSegment
{
    /// <summary>The text content to synthesize.</summary>
    public string Text { get; init; } = "";

    /// <summary>Speed multiplier (1.0 = normal). Mapped from prosody rate.</summary>
    public float Speed { get; init; } = 1.0f;

    /// <summary>Volume multiplier (1.0 = normal). Mapped from prosody volume.</summary>
    public float Volume { get; init; } = 1.0f;

    /// <summary>Pitch shift in semitones (0 = unchanged). Mapped from prosody pitch.</summary>
    public float PitchShift { get; init; } = 0f;

    /// <summary>Voice override for this segment, or null to use the speaker default.</summary>
    public string? Voice { get; init; }

    /// <summary>Duration of silence to insert before this segment.</summary>
    public TimeSpan BreakBefore { get; init; } = TimeSpan.Zero;

    /// <summary>Whether this is a break-only segment with no text.</summary>
    public bool IsBreak => string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether this segment uses emphasis (slower + louder).</summary>
    public bool Emphasis { get; init; }

    /// <summary>Emotion/style label to apply for this segment.</summary>
    public string? Emotion { get; init; }

    /// <summary>Emotion intensity multiplier (1.0 = default intensity).</summary>
    public float EmotionIntensity { get; init; } = 1.0f;
}
