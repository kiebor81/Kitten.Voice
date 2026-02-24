using System.Text;

namespace Kitten.Voice;

/// <summary>
/// English grapheme-to-phoneme converter.
/// Uses the CMU Pronouncing Dictionary (134,000+ words) with a rules-based fallback.
/// Produces IPA output compatible with the Kokoro/KittenTTS vocabulary.
/// </summary>
public static class EnglishToIpa
{
    private static readonly Dictionary<string, string> ArpabetToIpa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AA"] = "ɑː", ["AE"] = "æ", ["AH"] = "ʌ", ["AO"] = "ɔː",
        ["AW"] = "aʊ", ["AY"] = "aɪ", ["EH"] = "ɛ", ["ER"] = "ɜːɹ",
        ["EY"] = "eɪ", ["IH"] = "ɪ", ["IY"] = "iː", ["OW"] = "oʊ",
        ["OY"] = "ɔɪ", ["UH"] = "ʊ", ["UW"] = "uː",
        ["B"] = "b", ["CH"] = "tʃ", ["D"] = "d", ["DH"] = "ð",
        ["F"] = "f", ["G"] = "ɡ", ["HH"] = "h", ["JH"] = "dʒ",
        ["K"] = "k", ["L"] = "l", ["M"] = "m", ["N"] = "n",
        ["NG"] = "ŋ", ["P"] = "p", ["R"] = "ɹ", ["S"] = "s",
        ["SH"] = "ʃ", ["T"] = "t", ["TH"] = "θ", ["V"] = "v",
        ["W"] = "w", ["Y"] = "j", ["Z"] = "z", ["ZH"] = "ʒ",
    };

    private static readonly Dictionary<string, string> UnstressedVowelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AH"] = "ə", ["IH"] = "ɪ", ["AO"] = "ə", ["AA"] = "ə",
        ["EH"] = "ə", ["UH"] = "ə", ["ER"] = "ɚ",
    };

    private static readonly Dictionary<string, string> PronDict = LoadCmuDict();

    /// <summary>
    /// Pronunciation overrides — checked before CMUdict.
    /// Use ARPAbet format. Add entries here to fix edge cases.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    private static Dictionary<string, string> LoadCmuDict()
    {
        string path = Path.Combine("assets", "cmudict.dict");
        var dict = new Dictionary<string, string>(140000, StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
            return dict;

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";;;"))
                continue;

            int firstSpace = line.IndexOf(' ');
            if (firstSpace < 1) continue;

            string word = line[..firstSpace];
            string phones = line[(firstSpace + 1)..].Trim();

            if (word.Contains('(')) continue;

            dict.TryAdd(word, phones);
        }

        return dict;
    }

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

        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int w = 0; w < words.Length; w++)
        {
            if (w > 0) result.Append(' ');

            string clean = CleanWord(words[w]);

            if (Overrides.TryGetValue(clean, out string? over))
                result.Append(ArpabetToIpaString(over));
            else if (PronDict.TryGetValue(clean, out string? arpabet))
                result.Append(ArpabetToIpaString(arpabet));
            else
                result.Append(FallbackG2P(clean));
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

    private static string ArpabetToIpaString(string arpabet)
    {
        var result = new StringBuilder();

        foreach (string phone in arpabet.Split(' '))
        {
            int stress = -1;
            string basePhone = phone;
            if (phone.Length > 0 && char.IsDigit(phone[^1]))
            {
                stress = phone[^1] - '0';
                basePhone = phone[..^1];
            }

            if (stress == 1)
                result.Append('ˈ');
            else if (stress == 2)
                result.Append('ˌ');

            if (stress == 0 && UnstressedVowelMap.TryGetValue(basePhone, out string? reduced))
                result.Append(reduced);
            else if (ArpabetToIpa.TryGetValue(basePhone, out string? ipa))
                result.Append(ipa);
        }

        return result.ToString();
    }

    private static string FallbackG2P(string word)
    {
        var result = new StringBuilder();
        string lower = word.ToLowerInvariant();
        int i = 0;

        while (i < lower.Length)
        {
            string ipa = MatchRule(lower, ref i);
            result.Append(ipa);
        }

        return result.ToString();
    }

    private static string MatchRule(string word, ref int i)
    {
        char c = word[i];
        char next = i + 1 < word.Length ? word[i + 1] : '\0';
        char next2 = i + 2 < word.Length ? word[i + 2] : '\0';

        // Double consonants produce a single sound
        if (c == next && !"aeiou".Contains(c))
        {
            i++;
            return "";
        }

        // Multi-character rules (longest match first)
        if (c == 't' && next == 'h') { i += 2; return "θ"; }
        if (c == 's' && next == 'h') { i += 2; return "ʃ"; }
        if (c == 'c' && next == 'h') { i += 2; return "tʃ"; }
        if (c == 'n' && next == 'g') { i += 2; return "ŋ"; }
        if (c == 'p' && next == 'h') { i += 2; return "f"; }
        if (c == 'w' && next == 'h') { i += 2; return "w"; }
        if (c == 'c' && next == 'k') { i += 2; return "k"; }
        if (c == 'g' && next == 'h') { i += 2; return ""; }
        if (c == 'e' && next == 'e') { i += 2; return "iː"; }
        if (c == 'o' && next == 'o') { i += 2; return "uː"; }
        if (c == 'o' && next == 'u') { i += 2; return "aʊ"; }
        if (c == 'o' && next == 'w') { i += 2; return "oʊ"; }
        if (c == 'a' && next == 'i') { i += 2; return "eɪ"; }
        if (c == 'a' && next == 'y') { i += 2; return "eɪ"; }
        if (c == 'e' && next == 'a') { i += 2; return "iː"; }
        if (c == 'i' && next == 'g' && next2 == 'h') { i += 3; return "aɪ"; }
        if (c == 'o' && next == 'a') { i += 2; return "oʊ"; }

        // Silent final e
        if (c == 'e' && i == word.Length - 1) { i++; return ""; }

        i++;
        return c switch
        {
            'a' => "æ",
            'b' => "b",
            'c' => next is 'e' or 'i' or 'y' ? "s" : "k",
            'd' => "d",
            'e' => "ɛ",
            'f' => "f",
            'g' => "ɡ",
            'h' => "h",
            'i' => "ɪ",
            'j' => "dʒ",
            'k' => "k",
            'l' => "l",
            'm' => "m",
            'n' => "n",
            'o' => "ɑː",
            'p' => "p",
            'q' => "k",
            'r' => "ɹ",
            's' => "s",
            't' => "t",
            'u' => "ʌ",
            'v' => "v",
            'w' => "w",
            'x' => "ks",
            'y' => i == 1 ? "j" : "iː",
            'z' => "z",
            '\'' => "",
            _ => "",
        };
    }
}