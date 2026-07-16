using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>
/// 将空格和半角问号转换为 SQL Server LIKE 模式，其他字符按文本匹配。
/// </summary>
internal static class SqlLikePattern
{
    public static string Build(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return string.Empty;

        var pattern = new StringBuilder(keyword.Length + 2);
        pattern.Append('%');

        foreach (var character in keyword)
        {
            switch (character)
            {
                case '~':
                    pattern.Append("~~");
                    break;
                case '%':
                    pattern.Append("~%");
                    break;
                case '_':
                    pattern.Append("~_");
                    break;
                case '[':
                    pattern.Append("~[");
                    break;
                case ' ':
                    pattern.Append('%');
                    break;
                case '?':
                    pattern.Append('_');
                    break;
                default:
                    pattern.Append(character);
                    break;
            }
        }

        pattern.Append('%');

        return pattern.ToString();
    }
}
