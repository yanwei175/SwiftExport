using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>尚未提交的分配意图；持有选择时的版本依据，与已保存 Link 分开管理。</summary>
public sealed partial class ProcurementDemandAssociationPlanRow : ObservableObject
{
    /// <summary>本条意图在预览、提交和原命令重试期间保持不变的身份。</summary>
    public Guid CommandItemId { get; } = Guid.NewGuid();
    /// <summary>选择时的采购行依据；重读后须由用户重新选择候选复核。</summary>
    public required ProcurementDemandAssociationLineDto Line { get; init; }
    /// <summary>选择时的物料需求、来源修订与可分配数量依据。</summary>
    public required ProcurementDemandCandidateDto Demand { get; init; }
    [ObservableProperty] public partial decimal Quantity { get; set; }
    [ObservableProperty] public partial bool AcceptNonStandardMatch { get; set; }
    [ObservableProperty] public partial bool NeedsReview { get; set; }
    /// <summary>明确组装命令意图，不能把候选查询 DTO 直接作为写入负载。</summary>
    public ProcurementDemandAssociationItem ToItem() => new()
    {
        CommandItemId = CommandItemId, PurchaseOrderLineId = Line.PurchaseOrderLineId,
        ExpectedLineVersion = Line.Version, NetDemandId = Demand.NetDemandId,
        ExpectedDemandVersion = Demand.Version, ExpectedSourceDemandVersion = Demand.SourceDemandVersion,
        Quantity = Quantity, AcceptNonStandardMatch = AcceptNonStandardMatch
    };
}
