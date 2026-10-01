// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System.Text.RegularExpressions;

namespace SwiftExport.Application.Services.Systems.ExcelFieldMappings;

/// <summary>只约束映射配置标识的格式和数据库长度，不维护业务组或模型字段目录。</summary>
internal static class ExcelFieldMappingFormat
{
    private static readonly Regex Identifier = new(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex FieldPath = new(@"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.CultureInvariant);

    /// <summary>去除首尾空白并保留稳定组键大小写，避免改变已有配置的存储键。</summary>
    public static string? NormalizeGroup(string? groupCode)
    {
        var value = groupCode?.Trim();
        return value is not null && value.Length <= 40 && Identifier.IsMatch(value) ? value : null;
    }

    /// <summary>验证点分隔的 ASCII 标识及长度；字段是否允许赋值由客户端模型标记决定。</summary>
    public static bool IsValidField(string? fieldKey)
        => fieldKey is not null && fieldKey.Length <= 100 && FieldPath.IsMatch(fieldKey);
}
