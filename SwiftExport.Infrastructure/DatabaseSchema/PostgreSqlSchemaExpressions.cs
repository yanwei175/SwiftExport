using System.Text.RegularExpressions;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.DatabaseSchema;

/// <summary>只适配当前实体元数据的行内谓词子集；不翻译仓储SQL，遇到新语法立即失败。</summary>
internal static class PostgreSqlSchemaExpressions
{
    private static readonly Regex Token = new(@"\G\s*(N?'(?:''|[^'])*'|\[[^\]]+\]|[A-Za-z]+|\d+(?:\.\d+)?|<>|>=|<=|[=><(),+-])", RegexOptions.CultureInvariant);

    /// <summary>保持括号和逻辑运算；列按映射解析，字符串字面量不会被当成标识符替换。</summary>
    internal static string Convert(Type entity, string expression)
    {
        var source = EntityMetadataCache.GetMetadata(entity);
        var target = PostgreSqlEntityMetadata.GetMetadata(entity);
        var tokens = new List<string>();
        var position = 0;
        while (position < expression.Length && !string.IsNullOrWhiteSpace(expression[position..]))
        {
            var match = Token.Match(expression, position);
            if (!match.Success) throw new InvalidOperationException($"{entity.Name}含尚未适配的结构表达式：{expression}");
            tokens.Add(match.Groups[1].Value);
            position = match.Index + match.Length;
        }
        EntityColumnMetadata Column(string token)
        {
            if (!token.StartsWith('[')) throw new InvalidOperationException("结构函数只支持当前实体列参数。");
            return source.Columns.Single(c => c.ColumnName == token[1..^1]);
        }
        string Name(EntityColumnMetadata column) => PostgreSqlNames.Quote(target.GetProperty(column.PropertyName).ColumnName);
        var output = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token is "LEN" or "ISJSON")
            {
                if (i + 3 >= tokens.Count || tokens[i + 1] != "(" || tokens[i + 3] != ")")
                    throw new InvalidOperationException("结构函数参数尚未适配。");
                var column = Name(Column(tokens[i + 2]));
                if (token == "LEN") output.Add($"length(rtrim({column}, ' '))");
                else
                {
                    if (i + 5 >= tokens.Count || tokens[i + 4] != "=" || tokens[i + 5] != "1")
                        throw new InvalidOperationException("ISJSON仅支持当前元数据的=1形式。");
                    // SQL Server无类型约束的ISJSON只接受对象/数组，不能放宽为任意JSON标量。
                    output.Add($"({column} IS JSON OBJECT OR {column} IS JSON ARRAY)");
                    i += 2;
                }
                i += 3;
            }
            else if (token.StartsWith('['))
            {
                var column = Column(token);
                var name = Name(column);
                var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
                if (type == typeof(bool) && i + 2 < tokens.Count && tokens[i + 1] is "=" or "<>" && tokens[i + 2] is "0" or "1")
                {
                    output.Add($"{name} {tokens[i + 1]} {(tokens[i + 2] == "1" ? "TRUE" : "FALSE")}");
                    i += 2;
                }
                else output.Add(name);
            }
            else if (token.StartsWith("N'", StringComparison.Ordinal)) output.Add(token[1..]);
            else if (token.StartsWith('\'') || char.IsDigit(token[0]) || token is "AND" or "OR" or "NOT" or "IS" or "NULL" or "IN" or "(" or ")" or "," or "=" or "<>" or ">=" or "<=" or ">" or "<" or "+" or "-") output.Add(token);
            else throw new InvalidOperationException($"不支持结构表达式关键字：{token}");
        }
        return string.Join(" ", output);
    }
}
