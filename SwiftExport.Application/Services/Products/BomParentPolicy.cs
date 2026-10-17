using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products;

/// <summary>统一产品与 BOM 写入的父件资格及需要标记校验；供货方式不决定是否允许拥有 BOM。</summary>
public static class BomParentPolicy
{
    /// <summary>只有成品可作为 BOM 父件；不限制生产、采购、外协或启停状态。</summary>
    public static string? ValidateParent(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return string.Equals(product.MaterialType, MaterialTypesEnum.成品, StringComparison.Ordinal)
            ? null
            : $"物料 {product.Sku}（{product.MaterialType}）不能作为 BOM 父件，只有成品可以拥有 BOM。";
    }

    /// <summary>零标记表示不需要且不得带编号；正标记表示需要，仅成品允许，编号可空以等待建立或绑定。</summary>
    public static string? ValidateBinding(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.BomVersion < 0)
            return $"产品 {product.Sku} 的 BOM 有效标记不能为负数。";
        if (product.BomVersion == 0 && !string.IsNullOrWhiteSpace(product.BomNo))
            return $"产品 {product.Sku} 不需要 BOM 时不能保留 BOM 编号。";
        return product.BomVersion > 0 ? ValidateParent(product) : null;
    }
}
