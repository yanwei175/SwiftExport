using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Core.Interfaces.Products.Catalog;

/// <summary>提供物料资料读写及专用字段更新能力；业务授权、引用校验和跨仓储事务由应用服务负责。</summary>
public interface IProductRepository : IRepository<Product>
{
    /// <summary>在调用方事务中按 PG uid 顺序锁定物料，包装建立引用和物料删除/改型共用此协议。</summary>
    Task<IReadOnlyList<Product>> LockProductRowsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    /// <summary>读取物料未删除包装，左连接保留失效引用；写入时在物料锁之后按包装 uid 锁行。</summary>
    Task<IReadOnlyList<ProductPackageDto>> GetPackagesAsync(Guid ownerProductId, bool forUpdate = false, CancellationToken cancellationToken = default);
    /// <summary>在现有事务中新增当前物料的包装关系。</summary>
    Task<RepositoryWriteResult> AddPackageAsync(ProductPackage package, CancellationToken cancellationToken = default);
    /// <summary>按 Owner、行身份及版本更新或软删除包装；不得改写其他物料的关系。</summary>
    Task<RepositoryWriteResult> UpdatePackageAsync(ProductPackage package, CancellationToken cancellationToken = default);
    /// <summary>锁内临时释放待改型行的类型唯一键，支持类型互换；必须在同一事务恢复最终有效字典代码。</summary>
    Task<int> StagePackageTypeChangesAsync(Guid ownerProductId, IReadOnlyCollection<Guid> packageIds, CancellationToken cancellationToken = default);
    /// <summary>读取仍被未删除包装规格用作包材的物料身份；用于同一保存事务中的删除和改型保护。</summary>
    Task<IReadOnlyList<Guid>> GetReferencedPackagingMaterialIdsAsync(IEnumerable<Guid> productIds);
    /// <summary>读取物料详情完整资料和实时来源投影；标签选择由应用服务在同一读事务组装。</summary>
    Task<ProductDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>按来源主型号取得有效物料身份，供上游模板及删除引用保护使用。</summary>
    Task<IReadOnlyList<Guid>> GetIdsByProductMasterIdsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken = default);
    /// <summary>可取消地读取完整产品事实，供 BOM 绑定与校验使用。</summary>
    Task<Product?> GetByIdAsync(Guid uid, CancellationToken cancellationToken) => GetByIdAsync(uid);
    /// <summary>可取消地读取共享图引用的完整子产品，不扩展通用仓储职责。</summary>
    Task<IReadOnlyList<Product>> GetByIdListAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) => GetByIdListAsync(ids);
    /// <summary>在调用方事务内只更新所选产品，从其有效关联主型号读取指定资料组，返回实际变更产品数。</summary>
    Task<int> PullSelectedFieldsFromMastersAsync(
        IReadOnlyCollection<Guid> productIds,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default);
    /// <summary>返回未删除客户产品关系所关联的当前客户代码，关联产品和客户须未删除；保留停用记录并去重。</summary>
    Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync();
    Task<Product?> GetBySkuAsync(string sku);
    /// <summary>可取消的产品编码读取；当前 PostgreSQL 实现传递到实际数据库命令。</summary>
    Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken) => GetBySkuAsync(sku);
    /// <summary>按产品身份读取含当前制造商与建议仓库名称的详情投影。</summary>
    Task<ProductDto?> GetDtoByIdAsync(Guid uid);
    /// <summary>按 SKU 返回包含主型号实时分类的产品传输投影。</summary>
    Task<ProductDto?> GetDtoBySkuAsync(string sku);
    Task<Product?> GetByBomNoAsync(string bomNo);
    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList);
    Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request);
    /// <summary>可取消的子件候选搜索；兼容既有仓储消费者。</summary>
    Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request, CancellationToken cancellationToken) => SearchLookupAsync(request);
    Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList);
    Task<RepositoryWriteResult> SetDefaultSupplierAsync(
        Guid productId,
        int expectedVersion,
        Guid supplierId);
    Task<PagedResponseBase<SalesOrderProductLookupDto>> SearchSalesOrderProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default);
}

