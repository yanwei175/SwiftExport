using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Products.ProductMasters;

/// <summary>主型号仓储边界，提供稳定分页、聚合头部读取和编码占用查询。</summary>
public interface IProductMasterRepository : IRepository<ProductMaster>
{
    /// <summary>按关键词和分类集合分页读取主型号。</summary>
    Task<PagedResponseBase<ProductMasterListItemDto>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>读取主型号详情头部及实时分类、供应商名称。</summary>
    Task<ProductMasterDetailDto?> GetDetailHeaderAsync(Guid productMasterId, bool withUpdLock = false, CancellationToken cancellationToken = default);

    /// <summary>读取指定编码当前占用者，用于事务内唯一性校验。</summary>
    Task<IReadOnlyList<ProductMaster>> GetByMasterSkusAsync(IEnumerable<string> masterSkus, bool withUpdLock = false, CancellationToken cancellationToken = default);

    /// <summary>按主型号标识和预期版本更新分类，只触碰分类、审计字段和版本。</summary>
    Task<RepositoryWriteResult> AssignCategoryAsync(
        Guid productMasterId,
        int expectedVersion,
        Guid targetCategoryId,
        CancellationToken cancellationToken = default);
}
