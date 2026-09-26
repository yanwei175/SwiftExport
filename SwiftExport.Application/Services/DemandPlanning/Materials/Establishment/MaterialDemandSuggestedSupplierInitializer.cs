using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;

/// <summary>根据建立时读取到的有效产品和供应商主数据，为物料需求写入一次性初始采购建议。</summary>
public sealed class MaterialDemandSuggestedSupplierInitializer
{
    /// <summary>
    /// 将每条物料自己的有效默认供应商写入建议字段；无产品、无默认或默认供应商失效时保持为空。
    /// </summary>
    public void Apply(
        IReadOnlyList<NetDemand> demands,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Supplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(suppliers);

        foreach (var demand in demands)
        {
            demand.SuggestedPurchaseSupplierId = demand.ProductId is Guid productId
                && products.TryGetValue(productId, out var product)
                && product.DefaultSupplierId is Guid supplierId
                && suppliers.ContainsKey(supplierId)
                    ? supplierId
                    : null;
        }
    }
}
