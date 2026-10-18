using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;

/// <summary>提供供应商产品资料、可选 Product 投影及采购所需的真实产品匹配读取。</summary>
public interface ISupplierProductRepository : IRepository<SupplierProduct>
{
    Task<SupplierProductDto?> GetDtoByIdAsync(Guid uid);
    /// <summary>按供应商分页读取当前投影，透传取消至计数和取页命令。</summary>
    Task<PagedResponseBase<SupplierProductDto>> GetPagedAsync(SupplierProductSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetExistingSupplierSkuListAsync(IEnumerable<string> supplierSkus, Guid supplierId);
    Task<IReadOnlyList<string>> GetSupplierSkusUsedByOtherRecordsAsync(
        Guid supplierId,
        IEnumerable<(Guid Uid, string SupplierSku)> items);
    Task<IReadOnlyList<Guid>> GetSupplierIdsWithProductsAsync(IEnumerable<Guid> supplierIds);

    /// <summary>
    /// 按产品 Uid（物理列 SkuId）和供应商 Uid 批量读取采购 SKU 与价格资料。
    /// 返回全部活动匹配记录，重复主数据由 Application 明确识别，仓储不静默择一。
    /// </summary>
    Task<IReadOnlyList<SupplierProduct>> GetPurchaseDataAsync(
        IEnumerable<(Guid ProductId, Guid SupplierId)> keys);
}
