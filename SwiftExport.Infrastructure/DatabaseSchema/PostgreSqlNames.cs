using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SwiftExport.Infrastructure.DatabaseSchema;

/// <summary>统一PostgreSQL小写物理名称；仅处理标识符，不转换业务值或DTO字段。</summary>
public static class PostgreSqlNames
{
    /// <summary>将CLR名称转为ASCII snake_case；超长名称稳定缩短，调用方仍须检查碰撞。</summary>
    public static string FromClr(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var result = Regex.Replace(Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1_$2"), "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        if (!Regex.IsMatch(result, "^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"不支持的PostgreSQL标识符：{name}");
        if (result.Length <= 63) return result;
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)))[..12].ToLowerInvariant();
        return result[..50] + "_" + suffix;
    }

    /// <summary>引用已验证的物理标识符；关键字名称也不会被解析为SQL语法。</summary>
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
