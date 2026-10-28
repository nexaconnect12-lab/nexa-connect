namespace NexaConnect.POS;

// Draft editing only. Amount validation and financial authority stay in existing commands.
public static class TouchInputEdit
{
    public static string? Replace(string text, int start, int length, string insert, int maximum, bool amount, string separator = ".")
    {
        if (start < 0 || length < 0 || start > text.Length || length > text.Length - start) return null;
        string next = text.Remove(start, length).Insert(start, insert);
        if (next.Length > (maximum > 0 ? maximum : 4096)) return null;
        if (amount)
        {
            if (separator.Length != 1) return null;
            char mark = separator[0];
            if (next.Any(c => c is not (>= '0' and <= '9') && c != mark) ||
                next.Count(c => c == mark) > 1 || (next.IndexOf(mark) is var dot && dot >= 0 && next.Length - dot - 1 > 2)) return null;
        }
        return next;
    }

    public static int PreviousElement(string text, int caret)
    {
        if (caret <= 0) return 0;
        return System.Globalization.StringInfo.ParseCombiningCharacters(text)
            .LastOrDefault(index => index < caret);
    }
}
