namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>
/// 汇总来源退出涉及的 PackingList 与正式出库事实；由 Shipping 应用服务解释为稳定业务错误码。
/// </summary>
public sealed class SourceDeliveryExitAssessment
{
    /// <summary>仍引用来源需求的活动未过账 PackingList 草稿数。</summary>
    public int ActiveDraftCount { get; set; }

    /// <summary>仍引用来源需求且尚未反向的已过账 PackingList 数。</summary>
    public int PostedPackingListCount { get; set; }

    /// <summary>由相关 PackingList 触发且尚未反向的正式客户出库数。</summary>
    public int PostedCustomerOutboundCount { get; set; }

    /// <summary>孤立关系、非法状态组合或反向事实不完整的记录数。</summary>
    public int InvalidHistoryCount { get; set; }

    /// <summary>是否仍存在必须先处理的有效交付事实。</summary>
    public bool HasEffectiveDelivery =>
        ActiveDraftCount > 0 || PostedPackingListCount > 0 || PostedCustomerOutboundCount > 0;
}
