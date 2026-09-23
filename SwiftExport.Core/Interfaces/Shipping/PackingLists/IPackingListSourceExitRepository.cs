using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Core.Interfaces.Shipping.PackingLists;

/// <summary>在来源退出所属事务中核验装箱单与正式交付事实。</summary>
public interface IPackingListSourceExitRepository
{
    /// <summary>检查交付退出核验所需结构是否就绪。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken token = default);
    /// <summary>持锁汇总来源相关历史和有效交付，供来源命令决定能否退出。</summary>
    Task<SourceDeliveryExitAssessment> AssessSourceExitForUpdateAsync(IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken token = default);
}
