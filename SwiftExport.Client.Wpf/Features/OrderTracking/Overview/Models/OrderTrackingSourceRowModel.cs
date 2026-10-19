using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Mvvm;
namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>组合基础事实与独立摘要的只读显示行；请求状态不改变基础字段，结构字段由Mapster映射。</summary>
public sealed partial class OrderTrackingSourceRowModel : ObservableObjectEx
{
    [ObservableProperty] public partial Guid SourceGroupId { get; set; }
    [ObservableProperty] public partial string SourceType { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid SourceUid { get; set; }
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerName { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPo { get; set; } = string.Empty;
    [ObservableProperty] public partial string InvoiceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial int TotalDemandCount { get; set; }
    [ObservableProperty] public partial int MatchedDemandCount { get; set; }
    [ObservableProperty] public partial string ReviewReason { get; set; } = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DemandCountText), nameof(DeliveryText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief), nameof(DeliveryBrief), nameof(MaterialBrief), nameof(PurchaseBrief), nameof(ReceiptBrief))]
    public partial OrderTrackingSourceSummaryDto? Summary { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DemandCountText), nameof(DeliveryText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief), nameof(DeliveryBrief), nameof(MaterialBrief), nameof(PurchaseBrief), nameof(ReceiptBrief))]
    public partial TrackingSummaryLoadState SummaryState { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DemandCountText), nameof(DeliveryText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief), nameof(DeliveryBrief), nameof(MaterialBrief), nameof(PurchaseBrief), nameof(ReceiptBrief))]
    public partial TrackingSummaryAccess Access { get; set; } = TrackingSummaryAccess.None;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SummaryInfo), nameof(AttentionBrief), nameof(DeliveryBrief), nameof(MaterialBrief), nameof(PurchaseBrief), nameof(ReceiptBrief))]
    public partial DateTime? BusinessDate { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SummaryInfo), nameof(AttentionBrief), nameof(DeliveryBrief), nameof(MaterialBrief), nameof(PurchaseBrief), nameof(ReceiptBrief))]
    public partial DateTimeOffset? ReadAt { get; set; }
    /// <summary>基础总数、筛选命中数与独立摘要活动数分别显示。</summary>
    public string DemandCountText => $"全部 {TotalDemandCount} / 命中 {MatchedDemandCount}" + Environment.NewLine + "活动 " + (SummaryState == TrackingSummaryLoadState.Loaded && Summary is not null ? Summary.ActiveDemandCount.ToString() : SummaryState == TrackingSummaryLoadState.Failed ? "加载失败" : SummaryState == TrackingSummaryLoadState.Forbidden ? "无权" : "读取中");
    /// <summary>当前有权查看的关注项，空集合只表示没有可见关注项。</summary>
    public string AttentionText => TrackingSummaryText.Attention(SummaryState, Summary?.AttentionCodes, Access);
    /// <summary>保留摘要自己的读取时间及有权查看的核实说明。</summary>
    public string SummaryInfo => string.Join(Environment.NewLine, new[]
    {
        TrackingSummaryText.Stamp(BusinessDate, ReadAt), "关注：" + AttentionText, "交付：" + DeliveryText, "逾期：" + OverdueText, "物料需求：" + MaterialText, "采购承接：" + PurchaseText, "关联采购整行收货（不代表本需求实收份额）：" + ReceiptText, Summary?.ReviewReason,
        Access.Delivery && Summary?.DeliveryAvailability != "Forbidden" ? Summary?.DeliveryMessage : null,
        Access.Material && Summary?.MaterialAvailability != "Forbidden" ? Summary?.MaterialMessage : null,
        Access.Purchase && Summary?.PurchaseAvailability != "Forbidden" ? Summary?.PurchaseMessage : null,
        Access.Receipt && Summary?.ReceiptAvailability != "Forbidden" ? Summary?.ReceiptMessage : null
    }.Where(text => !string.IsNullOrWhiteSpace(text)));
    /// <summary>活动需求交付条数，绝不跨单位相加数量。</summary>
    public string DeliveryText => TrackingSummaryText.Read(SummaryState, Access.Delivery, Summary?.DeliveryAvailability, () => Summary!.ActiveDemandCount == 0 ? "无活动需求" : $"完成 {TrackingSummaryText.Count(Summary.DeliveryCompletedDemandCount)} / 未完 {TrackingSummaryText.Count(Summary.DeliveryOutstandingDemandCount)} / 核实 {TrackingSummaryText.Count(Summary.DeliveryReviewDemandCount)}");
    /// <summary>逾期条数与最长天数直接采用服务端结论。</summary>
    public string OverdueText => TrackingSummaryText.Read(SummaryState, Access.Delivery, Summary?.DeliveryAvailability, () => $"逾期 {TrackingSummaryText.Count(Summary!.OverdueDemandCount)} / 最长 {TrackingSummaryText.Count(Summary.MaxOverdueDays)} 天");
    /// <summary>无活动物料仅陈述事实，不推断漏建。</summary>
    public string MaterialText => TrackingSummaryText.Read(SummaryState, Access.Material, Summary?.MaterialAvailability, () => Summary!.ActiveDemandCount == 0 ? "无活动需求" : $"已建 {TrackingSummaryText.Count(Summary.MaterialEstablishedDemandCount)} / 无活动物料 {TrackingSummaryText.Count(Summary.NoActiveMaterialDemandCount)} / 核实 {TrackingSummaryText.Count(Summary.MaterialReviewDemandCount)}");
    /// <summary>采购承接是适用物料事实，不等同采购确认。</summary>
    public string PurchaseText => TrackingSummaryText.Read(SummaryState, Access.Purchase, Summary?.PurchaseAvailability, () => Summary!.ActiveDemandCount == 0 ? "无活动需求" : Summary.PurchaseApplicableDemandCount == 0 && Summary.PurchaseReviewDemandCount == 0 ? "不适用（无适用采购）" : $"不足 {TrackingSummaryText.Count(Summary.PurchaseShortfallDemandCount)} / 超量 {TrackingSummaryText.Count(Summary.PurchaseExcessDemandCount)} / 核实 {TrackingSummaryText.Count(Summary.PurchaseReviewDemandCount)}");
    /// <summary>采购行计数已由服务端去重，不累加各需求行计数。</summary>
    public string ReceiptText => TrackingSummaryText.Read(SummaryState, Access.Receipt, Summary?.ReceiptAvailability, () => Summary!.ReceiptPurchaseLineCount == 0 && Summary.ReceiptReviewLineCount == 0 ? "无关联采购行" : $"整行收足 {TrackingSummaryText.Count(Summary.ReceiptCompletedLineCount)} / 未收足 {TrackingSummaryText.Count(Summary.ReceiptOutstandingLineCount)} / 超收 {TrackingSummaryText.Count(Summary.ReceiptExcessLineCount)} / 核实 {TrackingSummaryText.Count(Summary.ReceiptReviewLineCount)}");
    /// <summary>来源行短标签，完整授权关注项在行提示内。</summary>
    public string AttentionBrief => TrackingSummaryText.BriefAttention(AttentionText);
    /// <summary>短交付条数保留未交及核实异常，不合计数量。</summary>
    public string DeliveryBrief => TrackingSummaryText.Read(SummaryState, Access.Delivery, Summary?.DeliveryAvailability, () => Summary!.ActiveDemandCount == 0 ? "无活动需求" : $"完成 {TrackingSummaryText.Count(Summary.DeliveryCompletedDemandCount)} / 未完 {TrackingSummaryText.Count(Summary.DeliveryOutstandingDemandCount)}" + TrackingSummaryText.ReviewSuffix(Summary.DeliveryReviewDemandCount));
    /// <summary>短物料条数不把无活动物料解释成漏建。</summary>
    public string MaterialBrief => TrackingSummaryText.Read(SummaryState, Access.Material, Summary?.MaterialAvailability, () => Summary!.ActiveDemandCount == 0 ? "无活动需求" : $"已建 {TrackingSummaryText.Count(Summary.MaterialEstablishedDemandCount)} / 无活动 {TrackingSummaryText.Count(Summary.NoActiveMaterialDemandCount)}" + TrackingSummaryText.ReviewSuffix(Summary.MaterialReviewDemandCount));
    /// <summary>短采购摘要仍显式保留不足、超量及核实。</summary>
    public string PurchaseBrief => TrackingSummaryText.Read(SummaryState, Access.Purchase, Summary?.PurchaseAvailability, () => Summary!.ActiveDemandCount == 0 || Summary.PurchaseApplicableDemandCount == 0 && Summary.PurchaseReviewDemandCount == 0 ? PurchaseText : $"不足 {TrackingSummaryText.Count(Summary.PurchaseShortfallDemandCount)} / 超量 {TrackingSummaryText.Count(Summary.PurchaseExcessDemandCount)}" + TrackingSummaryText.ReviewSuffix(Summary.PurchaseReviewDemandCount));
    /// <summary>短收货摘要依然是服务端去重的关联采购整行计数。</summary>
    public string ReceiptBrief => TrackingSummaryText.Read(SummaryState, Access.Receipt, Summary?.ReceiptAvailability, () => Summary!.ReceiptPurchaseLineCount == 0 && Summary.ReceiptReviewLineCount == 0 ? ReceiptText : $"收足 {TrackingSummaryText.Count(Summary.ReceiptCompletedLineCount)} / 未收足 {TrackingSummaryText.Count(Summary.ReceiptOutstandingLineCount)}" + (Summary.ReceiptExcessLineCount == 0 ? "" : $" · 超收 {TrackingSummaryText.Count(Summary.ReceiptExcessLineCount)}") + TrackingSummaryText.ReviewSuffix(Summary.ReceiptReviewLineCount));}
