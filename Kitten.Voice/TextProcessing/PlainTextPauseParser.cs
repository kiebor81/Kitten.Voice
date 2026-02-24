namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Parses plain-text pause cues (line breaks, ellipsis, em dash) into speakable segments.
/// </summary>
internal static class PlainTextPauseParser
{
    public static bool ContainsPauseCue(string text)
    {
        return text.IndexOfAny(['\r', '\n', '\u2026', '\u2014']) >= 0
            || text.Contains("...", StringComparison.Ordinal);
    }

    public static List<PlainTextPauseSegment> Split(
        string text,
        TimeSpan newlinePause,
        TimeSpan ellipsisPause,
        TimeSpan emDashPause)
    {
        var segments = new List<PlainTextPauseSegment>();
        var current = new System.Text.StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\u2014')
            {
                int dashes = ConsumeRepeatedChar(text, ref i, '\u2014');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(emDashPause, dashes)));
                current.Clear();
                continue;
            }

            if (c == '\u2026')
            {
                int ellipses = ConsumeRepeatedChar(text, ref i, '\u2026');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(ellipsisPause, ellipses)));
                current.Clear();
                continue;
            }

            if (c == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
            {
                int dotCount = ConsumeRepeatedChar(text, ref i, '.');
                int ellipsisCount = Math.Max(1, dotCount / 3);
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(ellipsisPause, ellipsisCount)));
                current.Clear();

                int remainder = dotCount % 3;
                for (int r = 0; r < remainder; r++)
                    current.Append('.');

                continue;
            }

            if (c is '\r' or '\n')
            {
                int breaks = 0;
                while (i < text.Length && text[i] is '\r' or '\n')
                {
                    if (text[i] == '\r')
                    {
                        breaks++;
                        if (i + 1 < text.Length && text[i + 1] == '\n')
                            i++;
                    }
                    else
                    {
                        breaks++;
                    }

                    i++;
                }

                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(newlinePause, breaks)));
                current.Clear();
                continue;
            }

            current.Append(c);
            i++;
        }

        segments.Add(new PlainTextPauseSegment(current.ToString(), TimeSpan.Zero));
        return segments;
    }

    private static int ConsumeRepeatedChar(string text, ref int index, char token)
    {
        int count = 0;
        while (index < text.Length && text[index] == token)
        {
            count++;
            index++;
        }

        return count;
    }

    private static TimeSpan ScalePauseByCount(TimeSpan basePause, int count)
    {
        int normalized = Math.Clamp(count, 1, 4);
        return TimeSpan.FromMilliseconds(basePause.TotalMilliseconds * normalized);
    }
}

internal readonly record struct PlainTextPauseSegment(string Text, TimeSpan PauseAfter);
