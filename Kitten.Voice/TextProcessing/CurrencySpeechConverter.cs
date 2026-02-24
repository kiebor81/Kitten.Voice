namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Provides functionality to convert currency expressions in text into their spoken word forms.
/// </summary>
internal static class CurrencySpeechConverter
{
    private readonly record struct CurrencyDescriptor(
        string SingularMajor,
        string PluralMajor,
        string SingularMinor,
        string PluralMinor,
        bool SupportsMinor = true);

    private static readonly Dictionary<char, CurrencyDescriptor> CurrencyDescriptors = new()
    {
        ['$'] = new("dollar", "dollars", "cent", "cents"),
        ['\u20AC'] = new("euro", "euros", "cent", "cents"),
        ['\u00A3'] = new("pound", "pounds", "penny", "pence"),
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
    /// Attempts to convert a currency expression at the specified position in the array of words into its spoken form.
    /// </summary>
    /// <param name="words"></param>
    /// <param name="index"></param>
    /// <param name="spoken"></param>
    /// <param name="consumedTokens"></param>
    /// <returns></returns>
    internal static bool TryConvertCurrencyAtPosition(string[] words, int index, out string spoken, out int consumedTokens)
    {
        spoken = string.Empty;
        consumedTokens = 0;

        if (TryConvertCurrencyToken(words[index], out string singleTokenWords))
        {
            spoken = singleTokenWords;
            consumedTokens = 1;
            return true;
        }

        if (index + 1 >= words.Length)
            return false;

        if (!TryConvertCurrencyCodeAmountPair(words[index], words[index + 1], out string pairWords))
            return false;

        spoken = pairWords;
        consumedTokens = 2;
        return true;
    }

    /// <summary>
    /// Attempts to convert a single token that may represent a currency expression into its spoken form.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="spoken"></param>
    /// <returns></returns>
    internal static bool TryConvertCurrencyToken(string token, out string spoken)
    {
        spoken = string.Empty;
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
        {
            return false;
        }

        if (!descriptor.SupportsMinor && hasMinorPart && minorUnits > 0)
            return false;

        spoken = BuildCurrencyWords(
            descriptor,
            majorDigits,
            minorUnits,
            hasMinorPart,
            explicitNegative || amountIsNegative);

        return !string.IsNullOrWhiteSpace(spoken);
    }

    private static bool TryConvertCurrencyCodeAmountPair(string firstToken, string secondToken, out string spoken)
    {
        spoken = string.Empty;
        string first = TrimTokenEnvelope(firstToken);
        string second = TrimTokenEnvelope(secondToken);
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        if (TryBuildCurrencyFromCodeAmountPair(first, second, out string forward))
        {
            spoken = forward;
            return true;
        }

        if (TryBuildCurrencyFromCodeAmountPair(second, first, out string reverse))
        {
            spoken = reverse;
            return true;
        }

        return false;
    }

    private static bool TryBuildCurrencyFromCodeAmountPair(string codeToken, string amountToken, out string spoken)
    {
        spoken = string.Empty;

        if (!TryGetCurrencyDescriptorByCodeToken(codeToken, out CurrencyDescriptor descriptor, out bool codeIsNegative))
            return false;

        if (!TryParseCurrencyAmount(
            amountToken,
            out string majorDigits,
            out int minorUnits,
            out bool hasMinorPart,
            out bool amountIsNegative))
        {
            return false;
        }

        if (!descriptor.SupportsMinor && hasMinorPart && minorUnits > 0)
            return false;

        spoken = BuildCurrencyWords(
            descriptor,
            majorDigits,
            minorUnits,
            hasMinorPart,
            codeIsNegative || amountIsNegative);

        return !string.IsNullOrWhiteSpace(spoken);
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
        {
            return TryParseCurrencyAmountWithSeparators(
                trimmed,
                groupSeparator: null,
                decimalSeparator: null,
                out majorDigits,
                out minorUnits,
                out hasMinorPart);
        }

        if (hasDot && hasComma)
        {
            int lastDot = trimmed.LastIndexOf('.');
            int lastComma = trimmed.LastIndexOf(',');

            if (lastComma > lastDot)
            {
                return TryParseCurrencyAmountWithSeparators(
                    trimmed,
                    groupSeparator: '.',
                    decimalSeparator: ',',
                    out majorDigits,
                    out minorUnits,
                    out hasMinorPart);
            }

            return TryParseCurrencyAmountWithSeparators(
                trimmed,
                groupSeparator: ',',
                decimalSeparator: '.',
                out majorDigits,
                out minorUnits,
                out hasMinorPart);
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
                && TryParseCurrencyAmountWithSeparators(
                    trimmed,
                    groupSeparator: separator,
                    decimalSeparator: null,
                    out majorDigits,
                    out minorUnits,
                    out hasMinorPart))
            {
                return true;
            }

            if (couldBeDecimal)
            {
                return TryParseCurrencyAmountWithSeparators(
                    trimmed,
                    groupSeparator: null,
                    decimalSeparator: separator,
                    out majorDigits,
                    out minorUnits,
                    out hasMinorPart);
            }

            return false;
        }

        return TryParseCurrencyAmountWithSeparators(
            trimmed,
            groupSeparator: separator,
            decimalSeparator: null,
            out majorDigits,
            out minorUnits,
            out hasMinorPart);
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

            normalized = EnglishNumberWords.NormalizeDigits(value);
            return true;
        }

        if (!value.Contains(groupSeparator.Value))
        {
            if (!value.All(char.IsDigit))
                return false;

            normalized = EnglishNumberWords.NormalizeDigits(value);
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

        normalized = EnglishNumberWords.NormalizeDigits(string.Concat(groups));
        return true;
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
            parts.Add(EnglishNumberWords.NumberToWords(majorDigits));
            parts.Add(majorIsOne ? descriptor.SingularMajor : descriptor.PluralMajor);
        }

        if (includeMinor)
        {
            if (!majorIsZero)
                parts.Add("and");

            bool minorIsOne = minorUnits == 1;
            parts.Add(EnglishNumberWords.NumberToWords(minorUnits.ToString()));
            parts.Add(minorIsOne ? descriptor.SingularMinor : descriptor.PluralMinor);
        }

        return string.Join(' ', parts);
    }
}
