using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>依据毛需求拆解历史，将来源取消划分为物理删除或作废留痕。</summary>
internal static class SourceDemandCancellationPolicy
{
    /// <summary>只对已安全回退的需求建立退出计划，历史不完整时显式阻断。</summary>
    public static SourceDemandCancellationPlan Build(IReadOnlyList<GrossDemand> demands, IReadOnlyDictionary<Guid, int> historyCounts)
    {
        var hardDeleteIds = new List<Guid>();
        var voidIds = new List<Guid>();
        foreach (var demand in demands)
        {
            if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active || demand.DecompositionStatus != GrossDemandDecompositionStatusEnum.Pending)
                throw new InvalidOperationException("毛需求当前状态不允许退出来源确认。");
            var historyCount = historyCounts.TryGetValue(demand.Uid, out var count) ? count : 0;
            if (demand.DecompositionRevision == 0 && historyCount == 0) hardDeleteIds.Add(demand.Uid);
            else if (demand.DecompositionRevision > 0 && historyCount > 0) voidIds.Add(demand.Uid);
            else throw new InvalidOperationException("毛需求拆解修订号与净需求历史不一致，不能退出来源确认。");
        }
        return new SourceDemandCancellationPlan(hardDeleteIds, voidIds);
    }
}

/// <summary>来源需求安全退出时的两组目标需求身份。</summary>
internal sealed class SourceDemandCancellationPlan(IReadOnlyList<Guid> hardDeleteGrossDemandIds, IReadOnlyList<Guid> voidGrossDemandIds)
{
    public IReadOnlyList<Guid> HardDeleteGrossDemandIds { get; } = hardDeleteGrossDemandIds;
    public IReadOnlyList<Guid> VoidGrossDemandIds { get; } = voidGrossDemandIds;
}
