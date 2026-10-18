namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>
/// 表示一条成品需求的物料历史退出评估；只描述 NetDemand 历史，不解释采购、入库或交付事实。
/// </summary>
public sealed class MaterialDemandSourceExitAssessment
{
    /// <summary>被评估的成品需求 Uid。</summary>
    public Guid GrossDemandId { get; set; }

    /// <summary>该成品需求下全部物料历史行数。</summary>
    public int HistoryCount { get; set; }

    /// <summary>是否仍存在活动物料修订。</summary>
    public bool HasActiveRevision { get; set; }

    /// <summary>全部历史是否均为合法的 RolledBack 或 Voided 修订。</summary>
    public bool IsHistoryComplete { get; set; }
}
