using System.Text;

internal static class CsvParser
{
    public static IReadOnlyList<string[]> Parse(string text, string fileName)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool closedQuote = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (closedQuote && character is not (',' or '\r' or '\n'))
            {
                throw Invalid(fileName, rows.Count + 1, "unexpected text after a closing quote");
            }

            switch (character)
            {
                case '"' when field.Length == 0 && !closedQuote:
                    quoted = true;
                    break;
                case '"':
                    throw Invalid(fileName, rows.Count + 1, "quote inside an unquoted field");
                case ',' :
                    row.Add(field.ToString());
                    field.Clear();
                    closedQuote = false;
                    break;
                case '\r' when index + 1 < text.Length && text[index + 1] == '\n':
                    index++;
                    AddRow(rows, row, field);
                    closedQuote = false;
                    break;
                case '\n':
                    AddRow(rows, row, field);
                    closedQuote = false;
                    break;
                case '\r':
                    throw Invalid(fileName, rows.Count + 1, "bare carriage returns are not supported");
                default:
                    field.Append(character);
                    break;
            }
        }

        if (quoted)
        {
            throw Invalid(fileName, rows.Count + 1, "unterminated quoted field");
        }

        if (field.Length > 0 || row.Count > 0 || closedQuote)
        {
            AddRow(rows, row, field);
        }

        return rows;
    }

    private static void AddRow(
        ICollection<string[]> rows,
        ICollection<string> row,
        StringBuilder field)
    {
        row.Add(field.ToString());
        rows.Add(row.ToArray());
        row.Clear();
        field.Clear();
    }

    private static DataGenerationException Invalid(string fileName, int row, string reason) =>
        new($"Invalid CSV in {fileName} at row {row}: {reason}.");
}
