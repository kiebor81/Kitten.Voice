namespace Kitten.Voice.Synthesis;

/// <summary>
/// Provides functionality to resolve emotional parameters for voice synthesis based on input emotion, intensity, and expressiveness.
/// </summary>
internal static class EmotionEngine
{
    private static readonly HashSet<string> DistortionProneEmotions = new(StringComparer.OrdinalIgnoreCase)
    {
        "excited",
        "fearful",
        "angry",
    };

    private readonly record struct EmotionProfile(float Volume, float Pitch, float Speed);

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

    /// <summary>
    /// Resolves the emotional parameters for voice synthesis based on the provided emotion, segment intensity, and expressiveness.
    /// </summary>
    /// <param name="VolumeMultiplier"></param>
    /// <param name="PitchSemitones"></param>
    /// <param name="SpeedMultiplier"></param>
    internal readonly record struct EmotionModifiers(float VolumeMultiplier, float PitchSemitones, float SpeedMultiplier);
    /// <summary>
    /// Represents the selection of an emotion style row and its associated blend factor for voice synthesis.
    /// </summary>
    /// <param name="Row"></param>
    /// <param name="Blend"></param>
    internal readonly record struct EmotionStyleSelection(int? Row, float Blend);
    /// <summary>
    /// Represents the resolved emotional parameters for voice synthesis, including whether the emotion is prone to distortion, the modifiers to apply, and the selected emotion style.
    /// </summary>
    /// <param name="DistortionProne"></param>
    /// <param name="Modifiers"></param>
    /// <param name="Style"></param>
    internal readonly record struct EmotionResolution(bool DistortionProne, EmotionModifiers Modifiers, EmotionStyleSelection Style);

    /// <summary>
    /// Resolves the emotional parameters for voice synthesis based on the input emotion, segment intensity, and expressiveness. This includes determining if the emotion is prone to distortion, calculating the appropriate modifiers for volume, pitch, and speed, and selecting the appropriate emotion style row and blend factor.
    /// </summary>
    /// <param name="emotion">The input emotion to resolve.</param>
    /// <param name="segmentIntensity">The intensity of the emotion segment.</param>
    /// <param name="expressiveness">The global expressiveness multiplier.</param>
    /// <returns>An <see cref="EmotionResolution"/> containing the resolved emotional parameters.</returns>
    internal static EmotionResolution Resolve(string? emotion, float segmentIntensity, float expressiveness)
    {
        float effectiveIntensity = ResolveEffectiveEmotionIntensity(segmentIntensity, expressiveness);
        EmotionModifiers modifiers = ResolveEmotionModifiers(emotion, effectiveIntensity);
        EmotionStyleSelection style = ResolveEmotionStyleSelection(emotion, effectiveIntensity);
        bool distortionProne = IsDistortionProneEmotion(emotion);
        return new EmotionResolution(distortionProne, modifiers, style);
    }

    private static float ResolveEffectiveEmotionIntensity(float segmentIntensity, float expressiveness)
    {
        float scalar = Math.Max(0f, expressiveness);
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
}
