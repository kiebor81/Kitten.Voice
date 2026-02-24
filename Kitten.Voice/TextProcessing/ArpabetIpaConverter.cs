using System.Text;

namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Converts ARPAbet phonetic transcriptions to IPA. This is a simplified mapping and may not cover all cases or dialects.
/// </summary>
internal static class ArpabetIpaConverter
{
    private static readonly Dictionary<string, string> ArpabetToIpa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AA"] = "\u0251\u02D0", ["AE"] = "\u00E6", ["AH"] = "\u028C", ["AO"] = "\u0254\u02D0",
        ["AW"] = "a\u028A", ["AY"] = "a\u026A", ["EH"] = "\u025B", ["ER"] = "\u025C\u02D0\u0279",
        ["EY"] = "e\u026A", ["IH"] = "\u026A", ["IY"] = "i\u02D0", ["OW"] = "o\u028A",
        ["OY"] = "\u0254\u026A", ["UH"] = "\u028A", ["UW"] = "u\u02D0",
        ["B"] = "b", ["CH"] = "t\u0283", ["D"] = "d", ["DH"] = "\u00F0",
        ["F"] = "f", ["G"] = "\u0261", ["HH"] = "h", ["JH"] = "d\u0292",
        ["K"] = "k", ["L"] = "l", ["M"] = "m", ["N"] = "n",
        ["NG"] = "\u014B", ["P"] = "p", ["R"] = "\u0279", ["S"] = "s",
        ["SH"] = "\u0283", ["T"] = "t", ["TH"] = "\u03B8", ["V"] = "v",
        ["W"] = "w", ["Y"] = "j", ["Z"] = "z", ["ZH"] = "\u0292",
    };

    private static readonly Dictionary<string, string> UnstressedVowelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AH"] = "\u0259", ["IH"] = "\u026A", ["AO"] = "\u0259", ["AA"] = "\u0259",
        ["EH"] = "\u0259", ["UH"] = "\u0259", ["ER"] = "\u025A",
    };

    /// <summary>
    /// Converts an ARPAbet transcription to IPA. It handles primary and secondary stress markers and reduces unstressed vowels to schwa where appropriate.
    /// </summary>
    /// <param name="arpabet"></param>
    /// <returns></returns>
    internal static string Convert(string arpabet)
    {
        var result = new StringBuilder();

        foreach (string phone in arpabet.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int stress = -1;
            string basePhone = phone;
            if (phone.Length > 0 && char.IsDigit(phone[^1]))
            {
                stress = phone[^1] - '0';
                basePhone = phone[..^1];
            }

            if (stress == 1)
                result.Append('\u02C8');
            else if (stress == 2)
                result.Append('\u02CC');

            if (stress == 0 && UnstressedVowelMap.TryGetValue(basePhone, out string? reduced))
                result.Append(reduced);
            else if (ArpabetToIpa.TryGetValue(basePhone, out string? ipa))
                result.Append(ipa);
        }

        return result.ToString();
    }
}
