using System.Text;

namespace Kitten.Voice.TextProcessing;

/// <summary>
/// A very basic fallback G2P for English, based on common pronunciation rules. This is not meant to be comprehensive or accurate, but should work reasonably well for simple words and names.
/// </summary>
internal static class EnglishFallbackG2P
{
    /// <summary>
    /// A very basic fallback G2P for English, based on common pronunciation rules. This is not meant to be comprehensive or accurate, but should work reasonably well for simple words and names.
    /// </summary>
    /// <param name="word"></param>
    /// <returns></returns>
    internal static string Convert(string word)
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
            return string.Empty;
        }

        // Multi-character rules (longest match first)
        if (c == 't' && next == 'h') { i += 2; return "\u03B8"; }
        if (c == 's' && next == 'h') { i += 2; return "\u0283"; }
        if (c == 'c' && next == 'h') { i += 2; return "t\u0283"; }
        if (c == 'n' && next == 'g') { i += 2; return "\u014B"; }
        if (c == 'p' && next == 'h') { i += 2; return "f"; }
        if (c == 'w' && next == 'h') { i += 2; return "w"; }
        if (c == 'c' && next == 'k') { i += 2; return "k"; }
        if (c == 'g' && next == 'h') { i += 2; return string.Empty; }
        if (c == 'e' && next == 'e') { i += 2; return "i\u02D0"; }
        if (c == 'o' && next == 'o') { i += 2; return "u\u02D0"; }
        if (c == 'o' && next == 'u') { i += 2; return "a\u028A"; }
        if (c == 'o' && next == 'w') { i += 2; return "o\u028A"; }
        if (c == 'a' && next == 'i') { i += 2; return "e\u026A"; }
        if (c == 'a' && next == 'y') { i += 2; return "e\u026A"; }
        if (c == 'e' && next == 'a') { i += 2; return "i\u02D0"; }
        if (c == 'i' && next == 'g' && next2 == 'h') { i += 3; return "a\u026A"; }
        if (c == 'o' && next == 'a') { i += 2; return "o\u028A"; }

        // Silent final e
        if (c == 'e' && i == word.Length - 1) { i++; return string.Empty; }

        i++;
        return c switch
        {
            'a' => "\u00E6",
            'b' => "b",
            'c' => next is 'e' or 'i' or 'y' ? "s" : "k",
            'd' => "d",
            'e' => "\u025B",
            'f' => "f",
            'g' => "\u0261",
            'h' => "h",
            'i' => "\u026A",
            'j' => "d\u0292",
            'k' => "k",
            'l' => "l",
            'm' => "m",
            'n' => "n",
            'o' => "\u0251\u02D0",
            'p' => "p",
            'q' => "k",
            'r' => "\u0279",
            's' => "s",
            't' => "t",
            'u' => "\u028C",
            'v' => "v",
            'w' => "w",
            'x' => "ks",
            'y' => i == 1 ? "j" : "i\u02D0",
            'z' => "z",
            '\'' => string.Empty,
            _ => string.Empty,
        };
    }
}
