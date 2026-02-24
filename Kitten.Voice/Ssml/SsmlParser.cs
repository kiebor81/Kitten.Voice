using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kitten.Voice.Ssml;

/// <summary>
/// Parses a subset of SSML into <see cref="SpeechSegment"/> objects.
/// Falls back to a single plain-text segment for non-SSML input.
/// </summary>
/// <remarks>
/// Supported tags:
/// <list type="bullet">
///   <item><c>&lt;speak&gt;</c> root element (optional)</item>
///   <item><c>&lt;break time="500ms"/&gt;</c> insert silence</item>
///   <item><c>&lt;prosody rate="fast" volume="loud" pitch="+2st"&gt;</c> modify speech</item>
///   <item><c>&lt;emphasis&gt;</c> slower speed + increased volume</item>
///   <item><c>&lt;voice name="Bella"&gt;</c> change voice for segment</item>
///   <item><c>&lt;say-as interpret-as="spell-out"&gt;</c> spell word letter by letter</item>
///   <item><c>&lt;emotion name="happy" intensity="strong"&gt;</c> apply emotional style</item>
///   <item><c>&lt;express-as style="calm" styledegree="1.2"&gt;</c> alias for emotion style</item>
/// </list>
/// </remarks>
internal static partial class SsmlParser
{
    /// <summary>
    /// Parses text (SSML or plain) into speech segments.
    /// </summary>
    internal static List<SpeechSegment> Parse(string input)
    {
        string trimmed = input.Trim();

        if (!trimmed.StartsWith('<'))
            return [new SpeechSegment { Text = trimmed }];

        // Wrap in <speak> if not already
        if (!trimmed.StartsWith("<speak", StringComparison.OrdinalIgnoreCase))
            trimmed = $"<speak>{trimmed}</speak>";

        var doc = XDocument.Parse(trimmed);
        var segments = new List<SpeechSegment>();
        ParseElement(
            doc.Root!,
            segments,
            speed: 1.0f,
            volume: 1.0f,
            pitch: 0f,
            voice: null,
            emphasis: false,
            emotion: null,
            emotionIntensity: 1.0f);
        return segments;
    }

    /// <summary>
    /// Returns true if the input looks like SSML (starts with &lt;).
    /// </summary>
    internal static bool IsSsml(string input) =>
        input.TrimStart().StartsWith('<');

    private static void ParseElement(
        XElement element,
        List<SpeechSegment> segments,
        float speed, float volume, float pitch,
        string? voice, bool emphasis,
        string? emotion, float emotionIntensity)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                string text = textNode.Value.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                float effectiveSpeed = emphasis ? speed * 0.85f : speed;
                float effectiveVolume = emphasis ? volume * 1.3f : volume;

                segments.Add(new SpeechSegment
                {
                    Text = text,
                    Speed = effectiveSpeed,
                    Volume = effectiveVolume,
                    PitchShift = pitch,
                    Voice = voice,
                    Emphasis = emphasis,
                    Emotion = emotion,
                    EmotionIntensity = emotionIntensity,
                });
            }
            else if (node is XElement child)
            {
                switch (child.Name.LocalName.ToLowerInvariant())
                {
                    case "break":
                        segments.Add(new SpeechSegment
                        {
                            BreakBefore = ParseDuration(child.Attribute("time")?.Value ?? "500ms"),
                        });
                        break;

                    case "prosody":
                        ParseElement(child, segments,
                            speed: ParseRate(child.Attribute("rate")?.Value, speed),
                            volume: ParseVolume(child.Attribute("volume")?.Value, volume),
                            pitch: ParsePitch(child.Attribute("pitch")?.Value, pitch),
                            voice,
                            emphasis,
                            emotion,
                            emotionIntensity);
                        break;

                    case "emphasis":
                        ParseElement(child, segments, speed, volume, pitch, voice, emphasis: true, emotion, emotionIntensity);
                        break;

                    case "voice":
                        ParseElement(child, segments, speed, volume, pitch,
                            voice: child.Attribute("name")?.Value ?? voice,
                            emphasis,
                            emotion,
                            emotionIntensity);
                        break;

                    case "say-as":
                        string? interpretAs = child.Attribute("interpret-as")?.Value;
                        string innerText = child.Value.Trim();

                        if (interpretAs?.Equals("spell-out", StringComparison.OrdinalIgnoreCase) == true)
                            innerText = string.Join(", ", innerText.Select(c => c.ToString()));

                        segments.Add(new SpeechSegment
                        {
                            Text = innerText,
                            Speed = speed,
                            Volume = volume,
                            PitchShift = pitch,
                            Voice = voice,
                            Emotion = emotion,
                            EmotionIntensity = emotionIntensity,
                        });
                        break;

                    case "emotion":
                    case "express-as":
                        ParseElement(
                            child,
                            segments,
                            speed,
                            volume,
                            pitch,
                            voice,
                            emphasis,
                            emotion: ParseEmotionName(child, emotion),
                            emotionIntensity: ParseEmotionIntensity(ParseEmotionIntensityAttr(child), emotionIntensity));
                        break;

                    default:
                        ParseElement(child, segments, speed, volume, pitch, voice, emphasis, emotion, emotionIntensity);
                        break;
                }
            }
        }
    }

    private static TimeSpan ParseDuration(string value)
    {
        var match = DurationRegex().Match(value);
        if (!match.Success) return TimeSpan.FromMilliseconds(500);

        double amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string unit = match.Groups[2].Value.ToLowerInvariant();

        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            _ => TimeSpan.FromMilliseconds(500),
        };
    }

    private static float ParseRate(string? value, float current)
    {
        if (string.IsNullOrEmpty(value)) return current;

        return value.ToLowerInvariant() switch
        {
            "x-slow" => current * 0.5f,
            "slow" => current * 0.75f,
            "medium" => current,
            "fast" => current * 1.25f,
            "x-fast" => current * 1.5f,
            _ => TryParsePercentage(value, current),
        };
    }

    private static float ParseVolume(string? value, float current)
    {
        if (string.IsNullOrEmpty(value)) return current;

        return value.ToLowerInvariant() switch
        {
            "silent" => 0f,
            "x-soft" => current * 0.25f,
            "soft" => current * 0.5f,
            "medium" => current,
            "loud" => current * 1.5f,
            "x-loud" => current * 2.0f,
            _ => TryParsePercentage(value, current),
        };
    }

    private static float ParsePitch(string? value, float current)
    {
        if (string.IsNullOrEmpty(value)) return current;

        return value.ToLowerInvariant() switch
        {
            "x-low" => current - 4f,
            "low" => current - 2f,
            "medium" => current,
            "high" => current + 2f,
            "x-high" => current + 4f,
            _ => TryParseSemitones(value, current),
        };
    }

    private static float TryParsePercentage(string value, float current)
    {
        var match = PercentageRegex().Match(value);
        if (match.Success)
            return current * float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100f;
        return current;
    }

    private static float TryParseSemitones(string value, float current)
    {
        var match = SemitonesRegex().Match(value);
        if (match.Success)
            return current + float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return current;
    }

    private static string? ParseEmotionName(XElement element, string? current)
    {
        string? value =
            element.Attribute("name")?.Value
            ?? element.Attribute("emotion")?.Value
            ?? element.Attribute("style")?.Value
            ?? element.Attribute("type")?.Value;

        return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
    }

    private static string? ParseEmotionIntensityAttr(XElement element) =>
        element.Attribute("intensity")?.Value
        ?? element.Attribute("level")?.Value
        ?? element.Attribute("styledegree")?.Value;

    private static float ParseEmotionIntensity(string? value, float current)
    {
        if (string.IsNullOrWhiteSpace(value))
            return current;

        string normalized = value.Trim().ToLowerInvariant();

        float parsed = normalized switch
        {
            "x-weak" => current * 0.6f,
            "weak" => current * 0.8f,
            "medium" => current,
            "strong" => current * 1.25f,
            "x-strong" => current * 1.5f,
            "none" => 0f,
            _ => ParseEmotionIntensityNumericOrPercent(normalized, current),
        };

        return Math.Clamp(parsed, 0f, 1.6f);
    }

    private static float ParseEmotionIntensityNumericOrPercent(string value, float current)
    {
        if (value.EndsWith('%'))
            return TryParsePercentage(value, current);

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scalar))
            return current * scalar;

        return current;
    }

    [GeneratedRegex(@"([\d.]+)\s*(ms|s)", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"([\d.]+)\s*%")]
    private static partial Regex PercentageRegex();

    [GeneratedRegex(@"([+-]?[\d.]+)\s*st", RegexOptions.IgnoreCase)]
    private static partial Regex SemitonesRegex();
}
