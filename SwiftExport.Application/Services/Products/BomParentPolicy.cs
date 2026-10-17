using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products;

/// <summary>统一产品与 BOM 写入的父件资格及绑定声明校验；供货方式不决定是否允许拥有 BOM。</summary>
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

    /// <summary>正标记与非空编号必须同时声明，未绑定只能使用零标记；声明绑定时同时检查父件资格。</summary>
    public static string? ValidateBinding(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.BomVersion < 0)
            return $"产品 {product.Sku} 的 BOM 有效标记不能为负数。";
        if ((product.BomVersion > 0) != !string.IsNullOrWhiteSpace(product.BomNo))
            return $"产品 {product.Sku} 的 BOM 编号和有效标记不一致；有 BOM 时编号不能为空且标记必须为正，无 BOM 时编号应为空且标记为零。";
        return product.BomVersion > 0 ? ValidateParent(product) : null;
    }
}
