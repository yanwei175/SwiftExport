using System.Text;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations.Internal;

internal static class ClipboardTsvSerializer
{
    public static string Serialize(IReadOnlyList<IReadOnlyList<string>> rows) =>
        string.Join("\r\n", rows.Select(row => string.Join('\t', row.Select(Escape))));

    public static bool TryParse(string text, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var result = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var endedWithRowDelimiter = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
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
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                endedWithRowDelimiter = false;
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    endedWithRowDelimiter = false;
                    break;
                case '\t':
                    row.Add(field.ToString());
                    field.Clear();
                    endedWithRowDelimiter = false;
                    break;
                case '\r':
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    result.Add(row);
                    row = [];
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    endedWithRowDelimiter = true;
                    break;
                default:
                    field.Append(character);
                    endedWithRowDelimiter = false;
                    break;
            }
        }

        if (inQuotes)
        {
            rows = [];
            return false;
        }

        if (!endedWithRowDelimiter || result.Count == 0)
        {
            row.Add(field.ToString());
            result.Add(row);
        }

        var width = result[0].Count;
        if (width == 0 || result.Any(candidate => candidate.Count != width))
        {
            rows = [];
            return false;
        }

        rows = result;
        return true;
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(['\t', '\r', '\n', '"']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
