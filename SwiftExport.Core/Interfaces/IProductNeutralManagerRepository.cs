using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces;

/// <summary>跨主型号中性产品管理仓储边界，提供管理页专用分页投影和主图替换能力。</summary>
public interface IProductNeutralManagerRepository : IRepository<ProductNeutral>
{
    /// <summary>按主型号分类、颜色和关键词分页读取中性产品管理投影。</summary>
    Task<PagedResponseBase<ProductNeutralManagerListItemDto>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定编码的当前占用者，用于事务内唯一性校验。</summary>
    Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(
        IEnumerable<string> productCodes,
        bool withUpdLock = false,
        CancellationToken cancellationToken = default);

    /// <summary>取消指定主型号下除提交行之外的默认主图，并返回实际更新行数。</summary>
    Task<int> ClearMainImagesAsync(
        Guid productMasterId,
        IReadOnlyCollection<Guid> excludedIds,
        CancellationToken cancellationToken = default);
}
