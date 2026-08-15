using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Application.Services.Products;

internal sealed record ProductUnitValidationItem(
    ProductDto Dto,
    string? ExistingUnit,
    int? RowIndex);

internal static class ProductUnitPolicy
{
    public static string? Normalize(
        IReadOnlyList<ProductUnitValidationItem> items,
        IReadOnlyList<SysDictData> dictionaryItems)
    {
        foreach (var item in items)
        {
            item.Dto.Unit = NormalizeOptional(item.Dto.Unit);
            if (item.Dto.Unit?.Length > 100)
                return $"第 {FormatRowIndex(item.RowIndex)}：基础单位不能超过 100 个字符。";
        }

        var enabledItems = dictionaryItems
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.DataNameEn))
            .ToArray();
        var duplicate = enabledItems
            .GroupBy(item => item.DataNameEn!.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            return $"计量单位字典存在重复的启用代码“{duplicate.Key}”，请先修复字典配置。";

        var activeUnits = enabledItems.ToDictionary(
            item => item.DataNameEn!.Trim(),
            item => item.DataNameEn!.Trim(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.Dto.Unit is null)
                continue;

            var existingUnit = NormalizeOptional(item.ExistingUnit);
            if (existingUnit is not null
                && string.Equals(existingUnit, item.Dto.Unit, StringComparison.OrdinalIgnoreCase)
                && !activeUnits.ContainsKey(item.Dto.Unit))
            {
                item.Dto.Unit = existingUnit;
                continue;
            }

            if (!activeUnits.TryGetValue(item.Dto.Unit, out var canonicalUnit))
                return $"第 {FormatRowIndex(item.RowIndex)}：基础单位“{item.Dto.Unit}”不存在、已停用或已删除。";
            item.Dto.Unit = canonicalUnit;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";
}
