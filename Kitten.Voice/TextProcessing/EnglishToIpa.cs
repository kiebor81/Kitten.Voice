using System.Text;

namespace Kitten.Voice.TextProcessing;

/// <summary>
/// English grapheme-to-phoneme converter.
/// Uses the CMU Pronouncing Dictionary (134,000+ words) with a rules-based fallback.
/// Produces IPA output compatible with the Kokoro/KittenTTS vocabulary.
/// </summary>
public static class EnglishToIpa
{
    private static readonly char[] HyphenSeparators = ['-', '\u2010', '\u2011', '\u2012', '\u2013'];

    private const string DefaultCmuDictFileName = "cmudict.dict";

    private static readonly object LexiconSync = new();
    private static CmuPronunciationLexicon CmuLexicon =
        CmuPronunciationLexicon.Load(GetDefaultLexiconPath(DefaultCmuDictFileName));

    private static readonly Dictionary<string, string> BuiltInOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    /// <summary>
    /// User-provided pronunciation overrides, merged with built-in overrides.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Overrides = BuiltInOverrides;

    /// <summary>
    /// Replaces pronunciation overrides used by the grapheme-to-phoneme converter.
    /// Keys are words; values are ARPAbet strings (for example: "K AE1 T").
    /// </summary>
    public static void SetOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            Overrides = BuiltInOverrides;
            return;
        }

        var merged = new Dictionary<string, string>(BuiltInOverrides, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in overrides)
        {
            string word = key?.Trim() ?? string.Empty;
            string arpabet = value?.Trim() ?? string.Empty;
            if (word.Length == 0 || arpabet.Length == 0)
                continue;

            merged[word] = arpabet;
        }

        Overrides = merged;
    }

    /// <summary>
    /// Configures the CMU pronunciation lexicon file path.
    /// </summary>
    public static void ConfigureLexicons(string? cmuDictPath)
    {
        string resolvedCmuPath = ResolveLexiconPath(cmuDictPath, DefaultCmuDictFileName);

        lock (LexiconSync)
        {
            CmuLexicon = CmuPronunciationLexicon.Load(resolvedCmuPath);
        }
    }

    private static string ResolveLexiconPath(string? configuredPath, string fallbackFileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        return GetDefaultLexiconPath(fallbackFileName);
    }

    private static string GetDefaultLexiconPath(string fileName) => Path.Combine("assets", fileName);

    /// <summary>
    /// Converts English text to IPA phonemes suitable for Kokoro/KittenTTS.
    /// </summary>
    public static string Convert(string text)
    {
        var result = new StringBuilder();
        string normalized = text.Trim();

        char? trailing = null;
        if (normalized.Length > 0 && ".,!?;:".Contains(normalized[^1]))
        {
            trailing = normalized[^1];
            normalized = normalized[..^1];
        }

        string[] words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int w = 0; w < words.Length; w++)
        {
            if (w > 0)
                result.Append(' ');

            if (CurrencySpeechConverter.TryConvertCurrencyAtPosition(words, w, out string spokenCurrency, out int consumed))
            {
                result.Append(ConvertWordsToPhonemes(spokenCurrency));
                w += consumed - 1;
            }
            else
            {
                result.Append(ConvertToken(words[w]));
            }
        }

        if (trailing.HasValue)
            result.Append(trailing.Value);

        return result.ToString();
    }

    private static string CleanWord(string word)
    {
        var sb = new StringBuilder();
        foreach (char c in word)
        {
            if (char.IsLetterOrDigit(c) || c == '\'')
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static string ConvertToken(string token)
    {
        if (CurrencySpeechConverter.TryConvertCurrencyToken(token, out string spokenCurrency))
            return ConvertWordsToPhonemes(spokenCurrency);

        if (TryConvertHyphenatedToken(token, out string hyphenatedPhonemes))
            return hyphenatedPhonemes;

        string clean = CleanWord(token);
        if (string.IsNullOrWhiteSpace(clean))
            return string.Empty;

        if (clean.All(char.IsDigit))
            return ConvertNumericToken(clean);

        return ConvertLexicalToken(clean);
    }

    private static bool TryConvertHyphenatedToken(string token, out string phonemes)
    {
        phonemes = string.Empty;
        if (!token.AsSpan().ContainsAny(HyphenSeparators))
            return false;

        string[] rawParts = token.Split(HyphenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawParts.Length < 2)
            return false;

        var convertedParts = new List<string>(rawParts.Length);
        for (int i = 0; i < rawParts.Length; i++)
        {
            string part = rawParts[i];
            string clean = CleanWord(part);
            if (string.IsNullOrWhiteSpace(clean))
                continue;

            string converted = ConvertHyphenPart(clean, i, rawParts.Length);
            if (converted.Length > 0)
                convertedParts.Add(converted);
        }

        if (convertedParts.Count < 2)
            return false;

        phonemes = string.Join(", ", convertedParts);
        return true;
    }

    private static string ConvertHyphenPart(string clean, int partIndex, int totalParts)
    {
        if (partIndex == 0
            && totalParts > 1
            && clean.Equals("re", StringComparison.OrdinalIgnoreCase))
        {
            // Prefer "ree-" for re- prefixes (e.g., "re-expose"), without changing standalone "re".
            return ArpabetIpaConverter.Convert("R IY0");
        }

        return clean.All(char.IsDigit)
            ? ConvertNumericToken(clean)
            : ConvertLexicalToken(clean);
    }

    private static string ConvertNumericToken(string digits)
    {
        string numberWords = EnglishNumberWords.NumberToWords(digits);
        if (string.IsNullOrWhiteSpace(numberWords))
            return string.Empty;

        return ConvertWordsToPhonemes(numberWords);
    }

    private static string ConvertWordsToPhonemes(string words)
    {
        string[] parts = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                result.Append(' ');

            result.Append(ConvertLexicalToken(parts[i]));
        }

        return result.ToString();
    }

    private static string ConvertLexicalToken(string clean)
    {
        if (Overrides.TryGetValue(clean, out string? over))
            return ArpabetIpaConverter.Convert(over);

        if (CmuLexicon.TryGetArpabet(clean, out string arpabet))
            return ArpabetIpaConverter.Convert(arpabet);

        return EnglishFallbackG2P.Convert(clean);
    }
}
