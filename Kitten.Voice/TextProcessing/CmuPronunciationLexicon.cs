namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Loads CMU dictionary entries and provides ARPAbet lookups.
/// This type is intentionally not wired into the active synthesis pipeline yet.
/// </summary>
internal sealed class CmuPronunciationLexicon
{
    private readonly Dictionary<string, string> _entries;

    private CmuPronunciationLexicon(Dictionary<string, string> entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Number of normalized entries loaded from the source dictionary.
    /// </summary>
    internal int Count => _entries.Count;

    /// <summary>
    /// Loads CMUdict data from disk.
    /// </summary>
    internal static CmuPronunciationLexicon Load(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
            return new CmuPronunciationLexicon(entries);

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";;;", StringComparison.Ordinal))
                continue;

            int firstSpace = line.IndexOf(' ');
            if (firstSpace < 1)
                continue;

            string key = NormalizeEntryKey(line[..firstSpace]);
            if (key.Length == 0 || entries.ContainsKey(key))
                continue;

            string arpabet = line[(firstSpace + 1)..].Trim();
            if (arpabet.Length == 0)
                continue;

            entries[key] = arpabet;
        }

        return new CmuPronunciationLexicon(entries);
    }

    /// <summary>
    /// Attempts to retrieve an ARPAbet pronunciation for a word.
    /// </summary>
    internal bool TryGetArpabet(string word, out string arpabet)
    {
        arpabet = string.Empty;
        string key = NormalizeEntryKey(word);
        if (key.Length == 0)
            return false;

        if (_entries.TryGetValue(key, out string? value))
        {
            arpabet = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes dictionary keys by dropping CMU-style variant suffixes such as "(1)".
    /// </summary>
    internal static string NormalizeEntryKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim();
        int openParen = trimmed.LastIndexOf('(');
        if (openParen <= 0 || trimmed[^1] != ')')
            return trimmed;

        ReadOnlySpan<char> variant = trimmed.AsSpan(openParen + 1, trimmed.Length - openParen - 2);
        return IsAllDigits(variant)
            ? trimmed[..openParen]
            : trimmed;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
            return false;

        foreach (char c in value)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return true;
    }
}
