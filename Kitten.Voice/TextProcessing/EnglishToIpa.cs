using System.Text;

namespace Kitten.Voice.TextProcessing;

/// <summary>
/// English grapheme-to-phoneme converter.
/// Uses the CMU Pronouncing Dictionary (134,000+ words) with a rules-based fallback.
/// Produces IPA output compatible with the Kokoro/KittenTTS vocabulary.
/// </summary>
public static class EnglishToIpa
{
    private readonly record struct CurrencyDescriptor(
        string SingularMajor,
        string PluralMajor,
        string SingularMinor,
        string PluralMinor,
        bool SupportsMinor = true);

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

    private static readonly Dictionary<string, string> BuiltInOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    /// <summary>
    /// User-provided pronunciation overrides, merged with built-in overrides.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Overrides = BuiltInOverrides;

    private static readonly string[] DigitWords =
    [
        "zero", "one", "two", "three", "four",
        "five", "six", "seven", "eight", "nine",
    ];

    private static readonly string[] TeenWords =
    [
        "ten", "eleven", "twelve", "thirteen", "fourteen",
        "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] TensWords =
    [
        "", "", "twenty", "thirty", "forty",
        "fifty", "sixty", "seventy", "eighty", "ninety",
    ];

    private static readonly Dictionary<char, CurrencyDescriptor> CurrencyDescriptors = new()
    {
        ['$'] = new("dollar", "dollars", "cent", "cents"),
        ['€'] = new("euro", "euros", "cent", "cents"),
        ['£'] = new("pound", "pounds", "penny", "pence"),
    };

    private static readonly Dictionary<string, CurrencyDescriptor> CurrencyCodeDescriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = new("dollar", "dollars", "cent", "cents"),
        ["EUR"] = new("euro", "euros", "cent", "cents"),
        ["GBP"] = new("pound", "pounds", "penny", "pence"),
        ["JPY"] = new("yen", "yen", "", "", SupportsMinor: false),
        ["AUD"] = new("dollar", "dollars", "cent", "cents"),
        ["CAD"] = new("dollar", "dollars", "cent", "cents"),
        ["NZD"] = new("dollar", "dollars", "cent", "cents"),
        ["SGD"] = new("dollar", "dollars", "cent", "cents"),
        ["HKD"] = new("dollar", "dollars", "cent", "cents"),
        ["CHF"] = new("franc", "francs", "centime", "centimes"),
        ["SEK"] = new("krona", "kronor", "ore", "ore"),
        ["NOK"] = new("krone", "kroner", "ore", "ore"),
        ["DKK"] = new("krone", "kroner", "ore", "ore"),
        ["CNY"] = new("yuan", "yuan", "fen", "fen"),
        ["RMB"] = new("yuan", "yuan", "fen", "fen"),
        ["INR"] = new("rupee", "rupees", "paise", "paise"),
    };

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

        string[] words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (int w = 0; w < words.Length; w++)
        {
            if (w > 0) result.Append(' ');

            if (TryConvertCurrencyAtPosition(words, w, out string currencyPhonemes, out int consumed))
            {
                result.Append(currencyPhonemes);
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
        if (TryConvertCurrencyToken(token, out string currencyPhonemes))
            return currencyPhonemes;

        string clean = CleanWord(token);
        if (string.IsNullOrWhiteSpace(clean))
            return string.Empty;

        if (clean.All(char.IsDigit))
            return ConvertNumericToken(clean);

        return ConvertLexicalToken(clean);
    }

    private static bool TryConvertCurrencyAtPosition(string[] words, int index, out string phonemes, out int consumedTokens)
    {
        phonemes = string.Empty;
        consumedTokens = 0;

        if (TryConvertCurrencyToken(words[index], out string singleTokenPhonemes))
        {
            phonemes = singleTokenPhonemes;
            consumedTokens = 1;
            return true;
        }

        if (index + 1 >= words.Length)
            return false;

        if (!TryConvertCurrencyCodeAmountPair(words[index], words[index + 1], out string pairPhonemes))
            return false;

        phonemes = pairPhonemes;
        consumedTokens = 2;
        return true;
    }

    private static string ConvertNumericToken(string digits)
    {
        string numberWords = NumberToWords(digits);
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

    private static bool TryConvertCurrencyToken(string token, out string phonemes)
    {
        phonemes = string.Empty;
        string core = TrimTokenEnvelope(token);
        if (string.IsNullOrWhiteSpace(core))
            return false;

        bool explicitNegative = false;
        if (core[0] is '-' or '+')
        {
            explicitNegative = core[0] == '-';
            core = core[1..];
        }

        if (!TryExtractCurrencyAndAmount(core, out CurrencyDescriptor descriptor, out string amount))
            return false;

        if (!TryParseCurrencyAmount(
            amount,
            out string majorDigits,
            out int minorUnits,
            out bool hasMinorPart,
            out bool amountIsNegative))
            return false;

        if (!descriptor.SupportsMinor && hasMinorPart && minorUnits > 0)
            return false;

        string spoken = BuildCurrencyWords(
            descriptor,
            majorDigits,
            minorUnits,
            hasMinorPart,
            explicitNegative || amountIsNegative);
        if (string.IsNullOrWhiteSpace(spoken))
            return false;

        phonemes = ConvertWordsToPhonemes(spoken);
        return phonemes.Length > 0;
    }

    private static bool TryConvertCurrencyCodeAmountPair(string firstToken, string secondToken, out string phonemes)
    {
        phonemes = string.Empty;
        string first = TrimTokenEnvelope(firstToken);
        string second = TrimTokenEnvelope(secondToken);
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        if (TryBuildCurrencyFromCodeAmountPair(first, second, out string forward))
        {
            phonemes = forward;
            return true;
        }

        if (TryBuildCurrencyFromCodeAmountPair(second, first, out string reverse))
        {
            phonemes = reverse;
            return true;
        }

        return false;
    }

    private static bool TryBuildCurrencyFromCodeAmountPair(string codeToken, string amountToken, out string phonemes)
    {
        phonemes = string.Empty;

        if (!TryGetCurrencyDescriptorByCodeToken(codeToken, out CurrencyDescriptor descriptor, out bool codeIsNegative))
            return false;

        if (!TryParseCurrencyAmount(
            amountToken,
            out string majorDigits,
            out int minorUnits,
            out bool hasMinorPart,
            out bool amountIsNegative))
            return false;

        if (!descriptor.SupportsMinor && hasMinorPart && minorUnits > 0)
            return false;

        string spoken = BuildCurrencyWords(
            descriptor,
            majorDigits,
            minorUnits,
            hasMinorPart,
            codeIsNegative || amountIsNegative);
        if (string.IsNullOrWhiteSpace(spoken))
            return false;

        phonemes = ConvertWordsToPhonemes(spoken);
        return phonemes.Length > 0;
    }

    private static string TrimTokenEnvelope(string token)
    {
        ReadOnlySpan<char> span = token.AsSpan().Trim();
        if (span.IsEmpty)
            return string.Empty;

        int start = 0;
        int end = span.Length - 1;

        while (start <= end && IsLeadingEnvelopeChar(span[start]))
            start++;

        while (end >= start && IsTrailingEnvelopeChar(span[end]))
            end--;

        return start > end ? string.Empty : span[start..(end + 1)].ToString();
    }

    private static bool IsLeadingEnvelopeChar(char c)
    {
        return c is '"' or '\'' or '(' or '[' or '{';
    }

    private static bool IsTrailingEnvelopeChar(char c)
    {
        return c is '"' or '\'' or ')' or ']' or '}' or '.' or ',' or '!' or '?' or ';' or ':';
    }

    private static bool TryExtractCurrencyAndAmount(string token, out CurrencyDescriptor descriptor, out string amount)
    {
        descriptor = default;
        amount = string.Empty;
        if (token.Length < 2)
            return false;

        if (TryGetCurrencyDescriptorBySymbol(token[0], out descriptor))
        {
            amount = token[1..];
            return !string.IsNullOrWhiteSpace(amount);
        }

        if (TryGetCurrencyDescriptorBySymbol(token[^1], out descriptor))
        {
            amount = token[..^1];
            return !string.IsNullOrWhiteSpace(amount);
        }

        if (TrySplitCodeAndAmount(token, out string code, out string amountValue)
            && TryGetCurrencyDescriptorByCode(code, out descriptor))
        {
            amount = amountValue;
            return true;
        }

        return false;
    }

    private static bool TryGetCurrencyDescriptorBySymbol(char symbol, out CurrencyDescriptor descriptor)
    {
        return CurrencyDescriptors.TryGetValue(symbol, out descriptor);
    }

    private static bool TryGetCurrencyDescriptorByCode(string code, out CurrencyDescriptor descriptor)
    {
        return CurrencyCodeDescriptors.TryGetValue(code, out descriptor);
    }

    private static bool TryGetCurrencyDescriptorByCodeToken(string token, out CurrencyDescriptor descriptor, out bool isNegative)
    {
        descriptor = default;
        isNegative = false;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string core = token;
        if (core[0] is '-' or '+')
        {
            isNegative = core[0] == '-';
            core = core[1..];
        }

        if (core.Length < 3 || !core.All(char.IsLetter))
            return false;

        return TryGetCurrencyDescriptorByCode(core, out descriptor);
    }

    private static bool TrySplitCodeAndAmount(string token, out string code, out string amount)
    {
        code = string.Empty;
        amount = string.Empty;
        if (token.Length < 4)
            return false;

        int leadingLetters = 0;
        while (leadingLetters < token.Length && char.IsLetter(token[leadingLetters]))
            leadingLetters++;

        if (leadingLetters >= 3 && leadingLetters < token.Length)
        {
            code = token[..leadingLetters];
            amount = token[leadingLetters..];
            return true;
        }

        int trailingLetters = 0;
        int i = token.Length - 1;
        while (i >= 0 && char.IsLetter(token[i]))
        {
            trailingLetters++;
            i--;
        }

        if (trailingLetters >= 3 && trailingLetters < token.Length)
        {
            code = token[(token.Length - trailingLetters)..];
            amount = token[..(token.Length - trailingLetters)];
            return true;
        }

        return false;
    }

    private static bool TryParseCurrencyAmount(
        string amount,
        out string majorDigits,
        out int minorUnits,
        out bool hasMinorPart,
        out bool isNegative)
    {
        majorDigits = string.Empty;
        minorUnits = 0;
        hasMinorPart = false;
        isNegative = false;

        string trimmed = amount.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed[0] is '-' or '+')
        {
            isNegative = trimmed[0] == '-';
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 0)
            return false;

        bool hasDot = trimmed.Contains('.');
        bool hasComma = trimmed.Contains(',');

        if (!hasDot && !hasComma)
            return TryParseCurrencyAmountWithSeparators(trimmed, null, null, out majorDigits, out minorUnits, out hasMinorPart);

        if (hasDot && hasComma)
        {
            int lastDot = trimmed.LastIndexOf('.');
            int lastComma = trimmed.LastIndexOf(',');

            if (lastComma > lastDot)
                return TryParseCurrencyAmountWithSeparators(trimmed, '.', ',', out majorDigits, out minorUnits, out hasMinorPart);

            return TryParseCurrencyAmountWithSeparators(trimmed, ',', '.', out majorDigits, out minorUnits, out hasMinorPart);
        }

        char separator = hasDot ? '.' : ',';
        int separatorCount = trimmed.Count(c => c == separator);
        if (separatorCount == 1)
        {
            int index = trimmed.IndexOf(separator);
            int digitsAfter = trimmed.Length - index - 1;
            bool couldBeDecimal = digitsAfter is 0 or 1 or 2;
            bool couldBeGrouped = digitsAfter == 3 && index > 0;

            if (couldBeGrouped
                && TryParseCurrencyAmountWithSeparators(trimmed, separator, null, out majorDigits, out minorUnits, out hasMinorPart))
            {
                return true;
            }

            if (couldBeDecimal)
                return TryParseCurrencyAmountWithSeparators(trimmed, null, separator, out majorDigits, out minorUnits, out hasMinorPart);

            return false;
        }

        return TryParseCurrencyAmountWithSeparators(trimmed, separator, null, out majorDigits, out minorUnits, out hasMinorPart);
    }

    private static bool TryParseCurrencyAmountWithSeparators(
        string amount,
        char? groupSeparator,
        char? decimalSeparator,
        out string majorDigits,
        out int minorUnits,
        out bool hasMinorPart)
    {
        majorDigits = string.Empty;
        minorUnits = 0;
        hasMinorPart = false;

        foreach (char c in amount)
        {
            if (char.IsDigit(c))
                continue;

            if ((groupSeparator.HasValue && c == groupSeparator.Value)
                || (decimalSeparator.HasValue && c == decimalSeparator.Value))
            {
                continue;
            }

            return false;
        }

        string majorPart = amount;
        string minorPart = string.Empty;

        if (decimalSeparator.HasValue)
        {
            int decimalIndex = amount.IndexOf(decimalSeparator.Value);
            if (decimalIndex >= 0)
            {
                if (amount.IndexOf(decimalSeparator.Value, decimalIndex + 1) >= 0)
                    return false;

                majorPart = amount[..decimalIndex];
                minorPart = amount[(decimalIndex + 1)..];
                hasMinorPart = true;
            }
        }

        if (string.IsNullOrEmpty(majorPart))
            majorPart = "0";

        if (!TryNormalizeGroupedDigits(majorPart, groupSeparator, out majorDigits))
            return false;

        if (!hasMinorPart || minorPart.Length == 0)
            return true;

        if (minorPart.Length > 2 || !minorPart.All(char.IsDigit))
            return false;

        int firstDigit = minorPart[0] - '0';
        minorUnits = minorPart.Length == 1
            ? firstDigit * 10
            : (firstDigit * 10) + (minorPart[1] - '0');

        return true;
    }

    private static bool TryNormalizeGroupedDigits(string value, char? groupSeparator, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!groupSeparator.HasValue)
        {
            if (!value.All(char.IsDigit))
                return false;

            normalized = NormalizeDigits(value);
            return true;
        }

        if (!value.Contains(groupSeparator.Value))
        {
            if (!value.All(char.IsDigit))
                return false;

            normalized = NormalizeDigits(value);
            return true;
        }

        string[] groups = value.Split(groupSeparator.Value, StringSplitOptions.None);
        if (groups.Length == 0)
            return false;

        for (int i = 0; i < groups.Length; i++)
        {
            string group = groups[i];
            if (group.Length == 0 || !group.All(char.IsDigit))
                return false;

            if (i == 0)
            {
                if (group.Length > 3)
                    return false;
            }
            else if (group.Length != 3)
            {
                return false;
            }
        }

        normalized = NormalizeDigits(string.Concat(groups));
        return true;
    }

    private static string NormalizeDigits(string digits)
    {
        string trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static string BuildCurrencyWords(
        CurrencyDescriptor descriptor,
        string majorDigits,
        int minorUnits,
        bool hasMinorPart,
        bool isNegative)
    {
        bool majorIsZero = majorDigits == "0";
        bool majorIsOne = majorDigits == "1";
        bool includeMinor = descriptor.SupportsMinor && hasMinorPart && minorUnits > 0;

        var parts = new List<string>();
        if (isNegative)
            parts.Add("minus");

        if (!majorIsZero || !includeMinor)
        {
            parts.Add(NumberToWords(majorDigits));
            parts.Add(majorIsOne ? descriptor.SingularMajor : descriptor.PluralMajor);
        }

        if (includeMinor)
        {
            if (!majorIsZero)
                parts.Add("and");

            bool minorIsOne = minorUnits == 1;
            parts.Add(NumberToWords((ulong)minorUnits));
            parts.Add(minorIsOne ? descriptor.SingularMinor : descriptor.PluralMinor);
        }

        return string.Join(' ', parts);
    }

    private static string ConvertLexicalToken(string clean)
    {
        if (Overrides.TryGetValue(clean, out string? over))
            return ArpabetToIpaString(over);

        if (PronDict.TryGetValue(clean, out string? arpabet))
            return ArpabetToIpaString(arpabet);

        return FallbackG2P(clean);
    }

    private static string NumberToWords(string digits)
    {
        string normalized = NormalizeDigits(digits);
        if (normalized.Length == 0)
            return "zero";

        if (!ulong.TryParse(normalized, out ulong value))
            return DigitsToWords(normalized);

        return NumberToWords(value);
    }

    private static string DigitsToWords(string digits)
    {
        var parts = new List<string>(digits.Length);
        foreach (char digit in digits)
            parts.Add(DigitWords[digit - '0']);
        return string.Join(' ', parts);
    }

    private static string NumberToWords(ulong value)
    {
        if (value == 0)
            return "zero";

        var parts = new List<string>();
        AppendScale(ref value, 1_000_000_000_000_000_000UL, "quintillion", parts);
        AppendScale(ref value, 1_000_000_000_000_000UL, "quadrillion", parts);
        AppendScale(ref value, 1_000_000_000_000UL, "trillion", parts);
        AppendScale(ref value, 1_000_000_000UL, "billion", parts);
        AppendScale(ref value, 1_000_000UL, "million", parts);
        AppendScale(ref value, 1_000UL, "thousand", parts);

        if (value > 0)
            parts.Add(NumberUnderThousand((int)value));

        return string.Join(' ', parts);
    }

    private static void AppendScale(ref ulong value, ulong scale, string name, List<string> parts)
    {
        if (value < scale)
            return;

        ulong chunk = value / scale;
        parts.Add($"{NumberUnderThousand((int)chunk)} {name}");
        value %= scale;
    }

    private static string NumberUnderThousand(int value)
    {
        if (value >= 100)
        {
            string hundreds = $"{DigitWords[value / 100]} hundred";
            int remainder = value % 100;
            return remainder == 0 ? hundreds : $"{hundreds} {NumberUnderHundred(remainder)}";
        }

        return NumberUnderHundred(value);
    }

    private static string NumberUnderHundred(int value)
    {
        if (value < 10)
            return DigitWords[value];

        if (value < 20)
            return TeenWords[value - 10];

        int tens = value / 10;
        int ones = value % 10;
        return ones == 0 ? TensWords[tens] : $"{TensWords[tens]} {DigitWords[ones]}";
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
