using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces;

/// <summary>中性产品仓储边界，限制明细始终在所属主型号聚合内读写。</summary>
public interface IProductNeutralRepository : IRepository<ProductNeutral>
{
    /// <summary>读取一个主型号下的全部有效中性产品及颜色名称。</summary>
    Task<IReadOnlyList<ProductNeutralDto>> GetByProductMasterIdAsync(Guid productMasterId, bool withUpdLock = false, CancellationToken cancellationToken = default);

    /// <summary>读取指定编码当前占用者，用于事务内唯一性校验。</summary>
    Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(IEnumerable<string> productCodes, bool withUpdLock = false, CancellationToken cancellationToken = default);

    /// <summary>在删除主型号时软删除其全部有效中性产品。</summary>
    Task<RepositoryWriteResult> SoftDeleteByProductMasterIdAsync(Guid productMasterId, CancellationToken cancellationToken = default);
}
