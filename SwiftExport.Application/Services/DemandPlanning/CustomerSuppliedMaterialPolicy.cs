using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>
/// 客供件领域约束的唯一实现。客供件身份完全由 Product 决定，BOM 只表达父子用量关系。
/// </summary>
public static class CustomerSuppliedMaterialPolicy
{
    public static string? ValidateProduct(Product product, bool hasActiveBom)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!IsCustomerSupplied(product))
            return null;
        if (!string.Equals(product.SupplyType, SupplyTypeEnums.采购, StringComparison.Ordinal))
            return $"客供件 {product.Sku} 的供货类型必须是采购。";
        if (product.BomVersion != 0 || !string.IsNullOrWhiteSpace(product.BomNo) || hasActiveBom)
            return $"客供件 {product.Sku} 必须是终端物料，不能维护有效 BOM。";
        return null;
    }

    public static string? ValidateBomParent(Product parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return IsCustomerSupplied(parent)
            ? $"客供件 {parent.Sku} 不能作为 BOM 父件。"
            : null;
    }

    public static string? ValidateBomLine(Bom line, Product child)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(child);

        if (line.Numerator <= 0 || line.Denominator <= 0)
            return $"BOM 第 {line.LineNumber} 行用量分子和分母必须大于零。";
        if (!line.SubSkuId.HasValue || line.SubSkuId.Value == Guid.Empty)
            return $"BOM 第 {line.LineNumber} 行必须关联有效子产品。";
        if (line.ParentSkuId.HasValue && line.ParentSkuId.Value == line.SubSkuId.Value)
            return $"BOM 第 {line.LineNumber} 行父件和子件不能相同。";
        if (IsCustomerSupplied(child)
            && !string.Equals(child.SupplyType, SupplyTypeEnums.采购, StringComparison.Ordinal))
            return $"客供件 {child.Sku} 的供货类型必须是采购。";
        return null;
    }

    public static bool IsCustomerSupplied(Product product)
        => string.Equals(product.MaterialType, MaterialTypesEnum.客供件, StringComparison.Ordinal);
}
