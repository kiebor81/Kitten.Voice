namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Splits plain text into chunks that satisfy a tokenizer length constraint.
/// </summary>
internal static class TextChunker
{
    public static List<string> SplitByTokenLimit(string text, int maxTokenCount, Func<string, int> getTokenCount)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return [];

        string[] units = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string unit in units)
        {
            string candidate = current.Length == 0 ? unit : $"{current} {unit}";
            if (getTokenCount(candidate) <= maxTokenCount)
            {
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(unit);
                continue;
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (getTokenCount(unit) <= maxTokenCount)
            {
                current.Append(unit);
                continue;
            }

            chunks.AddRange(SplitTokenByCharacterLimit(unit, maxTokenCount, getTokenCount));
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    private static List<string> SplitTokenByCharacterLimit(string token, int maxTokenCount, Func<string, int> getTokenCount)
    {
        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (char c in token)
        {
            string candidate = current.Length == 0 ? c.ToString() : $"{current}{c}";
            if (getTokenCount(candidate) <= maxTokenCount)
            {
                current.Append(c);
                continue;
            }

            if (current.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Input token '{token}' cannot be split to satisfy model token limit {maxTokenCount}.");
            }

            pieces.Add(current.ToString());
            current.Clear();
            current.Append(c);
        }

        if (current.Length > 0)
            pieces.Add(current.ToString());

        return pieces;
    }
}
