using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces.Products.Catalog;

/// <summary>提供物料资料读写及专用字段更新能力；业务授权、引用校验和跨仓储事务由应用服务负责。</summary>
public interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// 在当前事务内按 ProductMasterId 同步未软删除物料，包含停用物料；型号编码始终同步。
    /// updateDescriptions 控制中英文描述和配置，updateDefaultSupplier 控制供应商 ID；未选组保留原值，空值可清除。
    /// source 必须是服务端规范化后的主型号；仅实际变化的物料递增版本并更新审计字段，返回变更行数。
    /// </summary>
    Task<int> SyncFromProductMasterAsync(
        ProductMaster source,
        bool updateDescriptions,
        bool updateDefaultSupplier,
        CancellationToken cancellationToken = default);
    /// <summary>在调用方事务内从数据库当前主型号值向其未删除关联产品下发指定资料组，返回实际变更产品数。</summary>
    Task<int> SyncSelectedFieldsFromMastersAsync(
        IReadOnlyCollection<Guid> productMasterIds,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default);
    /// <summary>在调用方事务内只更新所选产品，从其有效关联主型号读取指定资料组，返回实际变更产品数。</summary>
    Task<int> PullSelectedFieldsFromMastersAsync(
        IReadOnlyCollection<Guid> productIds,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    /// <summary>按产品身份读取含当前制造商与建议仓库名称的详情投影。</summary>
    Task<ProductDto?> GetDtoByIdAsync(Guid uid);
    /// <summary>按 SKU 返回包含主型号实时分类的产品传输投影。</summary>
    Task<ProductDto?> GetDtoBySkuAsync(string sku);
    Task<Product?> GetByBomNoAsync(string bomNo);
    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList);
    Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request);
    Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request);
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
    Task<RepositoryWriteResult> SetDefaultSupplierAsync(
        Guid productId,
        int expectedVersion,
        Guid supplierId);
    Task<PagedResponseBase<SalesOrderProductLookupDto>> SearchSalesOrderProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default);
}

