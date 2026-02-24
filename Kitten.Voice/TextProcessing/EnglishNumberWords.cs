namespace Kitten.Voice.TextProcessing;

internal static class EnglishNumberWords
{
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

    internal static string NumberToWords(string digits)
    {
        string normalized = NormalizeDigits(digits);
        if (normalized.Length == 0)
            return "zero";

        if (!ulong.TryParse(normalized, out ulong value))
            return DigitsToWords(normalized);

        return NumberToWords(value);
    }

    internal static string NormalizeDigits(string digits)
    {
        string trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
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
}
