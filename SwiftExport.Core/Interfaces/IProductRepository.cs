using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces;

public interface IProductRepository : IRepository<Product>
{
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

