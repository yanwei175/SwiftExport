using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces;

/// <summary>提供物料资料读写及专用字段更新能力；业务授权、引用校验和跨仓储事务由应用服务负责。</summary>
public interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// 在当前事务内按主型号 Uid 同步未删除物料的型号编码及所选资料组，返回实际变更行数。
    /// source 必须是服务端规范化后的主型号，supplierCode 必须经过供应商引用校验；不更改关联本身。
    /// </summary>
    Task<int> SyncFromProductMasterAsync(
        ProductMaster source,
        string? supplierCode,
        bool updateDescriptions,
        bool updateDefaultSupplier,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    Task<Product?> GetByBomNoAsync(string bomNo);
    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList);
    Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request);
    Task<PagedResponseBase<ProductCategoryAssignmentRowDto>> GetCategoryAssignmentPageAsync(
        ProductCategoryAssignmentSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<RepositoryWriteResult> AssignCategoryAsync(
        Guid productId,
        int expectedVersion,
        Guid targetCategoryId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request);
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
    Task<RepositoryWriteResult> SetDefaultSupplierAsync(
        Guid productId,
        int expectedVersion,
        Guid supplierId,
        string supplierCode);
    Task<PagedResponseBase<SalesOrderProductLookupDto>> SearchSalesOrderProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default);
}

