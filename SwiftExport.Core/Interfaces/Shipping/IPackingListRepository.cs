using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;

namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>定义装箱单查询、默认包装补充、草稿锁定及持久化边界。</summary>
public interface IPackingListRepository
{
    /// <summary>验证当前数据库已部署新装箱单结构和草稿幂等约束。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default);
    /// <summary>分页读取装箱单管理摘要。</summary>
    Task<PagedResponseBase<PackingListManagerListItemDto>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取装箱单完整快照；不存在时返回空。</summary>
    Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>按来源标识读取唯一默认包装和可证明的销售客户采购号；不计算交付余额。</summary>
    Task<IReadOnlyList<PackingListSourceEnrichment>> GetSourceEnrichmentsAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);
    /// <summary>在调用方事务内锁定并汇总来源退出涉及的 PackingList 与正式客户出库事实。</summary>
    Task<SourceDeliveryExitAssessment> AssessSourceExitForUpdateAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);
    /// <summary>以事务级应用锁串行处理同一命令。</summary>
    Task<int> LockCommandAsync(Guid commandId, CancellationToken cancellationToken = default);
    /// <summary>锁定并读取单头，可选包含已软删除记录。</summary>
    Task<PackingListHead?> LockHeaderAsync(Guid uid, bool includeDeleted, CancellationToken cancellationToken = default);
    /// <summary>锁定并读取当前有效明细。</summary>
    Task<IReadOnlyList<PackingListLine>> LockLinesAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>锁定并读取给定行身份已占用的所属单据，用于拒绝跨单或已删除身份复用。</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> LockLineOwnersAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken = default);
    /// <summary>在调用方事务中持久化头、有效行和显式删除行。</summary>
    Task SaveAsync(PackingListHead header, IReadOnlyList<PackingListLine> activeLines,
        IReadOnlyList<PackingListLine> deletedLines, int expectedVersion, CancellationToken cancellationToken = default);
    /// <summary>在调用方事务中软删除活动未过账草稿及其有效行。</summary>
    Task DeleteAsync(PackingListHead header, CancellationToken cancellationToken = default);
}

/// <summary>PackingList 对统一来源投影的模块专属补充资料。</summary>
public sealed class PackingListSourceEnrichment
{
    /// <summary>来源需求标识。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>唯一默认包装的每包数量；零条或多条默认包装时为空。</summary>
    public decimal? PcsPerPackage { get; set; }
    /// <summary>唯一默认包装的单包净重。</summary>
    public decimal? NwPerPackage { get; set; }
    /// <summary>唯一默认包装的单包毛重。</summary>
    public decimal? GwPerPackage { get; set; }
    /// <summary>唯一默认包装的单包体积。</summary>
    public decimal? CbmPerPackage { get; set; }
    /// <summary>仅在 SO 头行身份均匹配时采用的客户采购号。</summary>
    public string? CustomerPoNo { get; set; }
}
