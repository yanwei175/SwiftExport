using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单取消确认时的毛需求退出分类；不访问数据库。
/// </summary>
internal static class SalesOrderCancellationPolicy
{
    public static SalesOrderCancellationPlan Build(
        IReadOnlyList<GrossDemand> demands,
        IReadOnlyDictionary<Guid, int> netDemandHistoryCounts)
    {
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(netDemandHistoryCounts);

        var hardDeleteIds = new List<Guid>();
        var voidIds = new List<Guid>();
        foreach (var demand in demands)
        {
            if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active
                || demand.DecompositionStatus != GrossDemandDecompositionStatusEnum.Pending)
            {
                throw new InvalidOperationException("毛需求当前状态不允许取消销售订单确认。");
            }

            var historyCount = netDemandHistoryCounts.TryGetValue(demand.Uid, out var count) ? count : 0;
            if (demand.DecompositionRevision == 0 && historyCount == 0)
            {
                hardDeleteIds.Add(demand.Uid);
                continue;
            }

            if (demand.DecompositionRevision > 0 && historyCount > 0)
            {
                voidIds.Add(demand.Uid);
                continue;
            }

            throw new InvalidOperationException("毛需求拆解修订号与净需求历史不一致，不能取消确认。");
        }

        return new SalesOrderCancellationPlan(hardDeleteIds, voidIds);
    }
}

internal sealed class SalesOrderCancellationPlan(
    IReadOnlyList<Guid> hardDeleteGrossDemandIds,
    IReadOnlyList<Guid> voidGrossDemandIds)
{
    public IReadOnlyList<Guid> HardDeleteGrossDemandIds { get; } = hardDeleteGrossDemandIds;
    public IReadOnlyList<Guid> VoidGrossDemandIds { get; } = voidGrossDemandIds;
}
