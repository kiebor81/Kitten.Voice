namespace Kitten.Voice.TextProcessing;

/// <summary>
/// Parses plain-text pause cues (line breaks, ellipsis, em dash, comma, colon, semicolon, sentence punctuation)
/// into speakable segments.
/// </summary>
internal static class PlainTextPauseParser
{
    /// <summary>
    /// Checks if the text contains any pause cues that would require splitting into segments.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    internal static bool ContainsPauseCue(string text)
    {
        return text.IndexOfAny(['\r', '\n', '\u2026', '\u2014', ',', ';', ':', '.', '?', '!']) >= 0
            || text.Contains("...", StringComparison.Ordinal);
    }

    internal static List<PlainTextPauseSegment> Split(
        string text,
        TimeSpan newlinePause,
        TimeSpan ellipsisPause,
        TimeSpan emDashPause,
        TimeSpan commaPause,
        TimeSpan semicolonPause,
        TimeSpan colonPause,
        TimeSpan periodPause,
        TimeSpan questionPause,
        TimeSpan exclamationPause)
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

            if (c == '.')
            {
                if (i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
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

                if (IsIntraNumericPunctuation(text, i) || IsLikelyDotJoiner(text, i))
                {
                    current.Append('.');
                    i++;
                    continue;
                }

                int periods = ConsumeRepeatedChar(text, ref i, '.');
                current.Append('.');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(periodPause, periods)));
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

            if (c == '?')
            {
                int questions = ConsumeRepeatedChar(text, ref i, '?');
                current.Append('?');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(questionPause, questions)));
                current.Clear();
                continue;
            }

            if (c == '!')
            {
                int exclamations = ConsumeRepeatedChar(text, ref i, '!');
                current.Append('!');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(exclamationPause, exclamations)));
                current.Clear();
                continue;
            }

            if (c == ',' && !IsIntraNumericPunctuation(text, i))
            {
                int commas = ConsumeRepeatedChar(text, ref i, ',');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(commaPause, commas)));
                current.Clear();
                continue;
            }

            if (c == ';')
            {
                int semicolons = ConsumeRepeatedChar(text, ref i, ';');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(semicolonPause, semicolons)));
                current.Clear();
                continue;
            }

            if (c == ':' && !IsIntraNumericPunctuation(text, i) && !IsLikelyUrlSchemeSeparator(text, i))
            {
                int colons = ConsumeRepeatedChar(text, ref i, ':');
                segments.Add(new PlainTextPauseSegment(current.ToString(), ScalePauseByCount(colonPause, colons)));
                current.Clear();
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

    private static bool IsIntraNumericPunctuation(string text, int index)
    {
        if (index <= 0 || index >= text.Length - 1)
            return false;

        return char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);
    }

    private static bool IsLikelyUrlSchemeSeparator(string text, int index)
    {
        if (index < 2 || index + 2 >= text.Length)
            return false;

        return text[index + 1] == '/'
            && text[index + 2] == '/'
            && char.IsLetter(text[index - 1]);
    }

    private static bool IsLikelyDotJoiner(string text, int index)
    {
        if (index <= 0 || index >= text.Length - 1)
            return false;

        return IsDotJoinerChar(text[index - 1]) && IsDotJoinerChar(text[index + 1]);
    }

    private static bool IsDotJoinerChar(char c) => char.IsLetterOrDigit(c) || c == '-';
}

/// <summary>
/// Represents a segment of text along with the pause duration that should follow it when spoken.
/// </summary>
/// <param name="Text">The text of the segment.</param>
/// <param name="PauseAfter">The duration of the pause that should follow the segment.</param>
internal readonly record struct PlainTextPauseSegment(string Text, TimeSpan PauseAfter);
