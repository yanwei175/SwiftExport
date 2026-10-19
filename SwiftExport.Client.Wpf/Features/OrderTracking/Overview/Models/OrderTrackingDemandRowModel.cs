using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Mvvm;
namespace SwiftExportClient.Features.OrderTracking.Overview.Models;
/// <summary>组合基础事实与独立摘要的只读显示行；请求状态不改变基础字段，结构字段由Mapster映射。</summary>
public sealed partial class OrderTrackingDemandRowModel : ObservableObjectEx
{
    [ObservableProperty] public partial Guid GrossDemandId { get; set; }
    [ObservableProperty] public partial int Version { get; set; }
    [ObservableProperty] public partial string SourceType { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid SourceUid { get; set; }
    [ObservableProperty] public partial Guid SourceLineId { get; set; }
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial int? SourceLineNumber { get; set; }
    [ObservableProperty] public partial Guid? CustomerId { get; set; }
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerName { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPo { get; set; } = string.Empty;
    [ObservableProperty] public partial string InvoiceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? ProductId { get; set; }
    [ObservableProperty] public partial bool IsNonStandard { get; set; }
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImagePath { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial string Color { get; set; } = string.Empty;
    [ObservableProperty] public partial string Specification { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal DemandQty { get; set; }
    [ObservableProperty] public partial string Unit { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? RequiredDate { get; set; }
    [ObservableProperty] public partial GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DeliveredText), nameof(RemainingText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief))]
    public partial OrderTrackingDemandSummaryDto? Summary { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DeliveredText), nameof(RemainingText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief))]
    public partial TrackingSummaryLoadState SummaryState { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DeliveredText), nameof(RemainingText), nameof(OverdueText), nameof(MaterialText), nameof(PurchaseText), nameof(ReceiptText), nameof(AttentionText), nameof(SummaryInfo), nameof(AttentionBrief))]
    public partial TrackingSummaryAccess Access { get; set; } = TrackingSummaryAccess.None;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SummaryInfo), nameof(AttentionBrief))]
    public partial DateTime? BusinessDate { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SummaryInfo), nameof(AttentionBrief))]
    public partial DateTimeOffset? ReadAt { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MatchText))]
    public partial bool? QueryMatched { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MatchText))]
    public partial TrackingSummaryLoadState MatchState { get; set; }
    /// <summary>仅展示服务端命中结论，未知或失败绝不当作未命中。</summary>
    public string MatchText => MatchState switch
    { TrackingSummaryLoadState.Loaded => QueryMatched == true ? "命中" : "未命中", TrackingSummaryLoadState.Loading => "定位中…", TrackingSummaryLoadState.Failed => "定位失败", TrackingSummaryLoadState.Forbidden => "无权", _ => "未定位" };
    /// <summary>当前有权查看的关注项，空集合只表示没有可见关注项。</summary>
    public string AttentionBrief => TrackingSummaryText.BriefAttention(AttentionText);
    /// <summary>完整授权关注标签与版本状态。</summary>
    public string AttentionText => SummaryState == TrackingSummaryLoadState.Loaded && Summary?.DemandVersion != Version ? "摘要需刷新" : TrackingSummaryText.Attention(SummaryState, Summary?.AttentionCodes, Access);
    /// <summary>保留摘要自己的读取时间及有权查看的核实说明。</summary>
    public string SummaryInfo => string.Join(Environment.NewLine, new[]
    {
        TrackingSummaryText.Stamp(BusinessDate, ReadAt), "关注：" + AttentionText, "已交：" + DeliveredText, "剩余未交：" + RemainingText, "逾期：" + OverdueText, "物料需求：" + MaterialText, "采购承接：" + PurchaseText, "关联采购整行收货（不代表本需求实收份额）：" + ReceiptText, Summary?.ReviewReason,
        Access.Delivery && Summary?.DeliveryAvailability != "Forbidden" ? Summary?.DeliveryMessage : null,
        Access.Material && Summary?.MaterialAvailability != "Forbidden" ? Summary?.MaterialMessage : null,
        Access.Purchase && Summary?.PurchaseAvailability != "Forbidden" ? Summary?.PurchaseMessage : null,
        Access.Receipt && Summary?.ReceiptAvailability != "Forbidden" ? Summary?.ReceiptMessage : null
    }.Where(text => !string.IsNullOrWhiteSpace(text)));
    private string ReadDimension(bool allowed, string? availability, Func<string> read) => TrackingSummaryText.Read(SummaryState, allowed, availability, () => Summary!.DemandVersion != Version ? "摘要需刷新" : read());
    private string DeliveryValue(Func<string> read) => ReadDimension(Access.Delivery, Summary?.DeliveryAvailability, () => Summary!.IsActive ? read() : "不适用（非活动）");
    /// <summary>可信已交数量保留零与未知，不因缺交期而隐藏。</summary>
    public string DeliveredText => DeliveryValue(() => Summary!.DeliveredQty is null ? "待核实" : $"{TrackingSummaryText.Qty(Summary.DeliveredQty)} {Summary.Unit}");
    /// <summary>可信未交数量保留异常负数，不截成零。</summary>
    public string RemainingText => DeliveryValue(() => Summary!.RemainingQty is null ? "待核实" : $"{TrackingSummaryText.Qty(Summary.RemainingQty)} {Summary.Unit}");
    /// <summary>未知逾期结论不显示为未逾期。</summary>
    public string OverdueText => DeliveryValue(() => Summary!.IsOverdue is null ? "待核实" : Summary.IsOverdue.Value ? $"逾期 {TrackingSummaryText.Count(Summary.OverdueDays)} 天" : "未逾期");
    /// <summary>分别显示活动物料与历史物料条数。</summary>
    public string MaterialText => ReadDimension(Access.Material, Summary?.MaterialAvailability, () => $"{(Summary!.IsActive ? "活动" : "非活动")} {TrackingSummaryText.Count(Summary.ActiveMaterialCount)} / 历史 {TrackingSummaryText.Count(Summary.HistoricalMaterialCount)} / 核实 {TrackingSummaryText.Count(Summary.MaterialReviewCount)}");
    /// <summary>承接状态使用服务端状态，不由行数或零数量推导完成。</summary>
    public string PurchaseText => ReadDimension(Access.Purchase, Summary?.PurchaseAvailability, () => !Summary!.IsActive ? "不适用（非活动）" : Summary.PurchaseStatus switch
    { "NotApplicable" => "不适用", "Unallocated" => "未承接", "Partial" => "部分承接", "Allocated" => "已承接（非确认）", "Excess" => "承接超量", "Review" => "待核实", "Inactive" => "不适用（非活动）", _ => "未知" });
    /// <summary>关联采购整行计数不是本需求实收份额。</summary>
    public string ReceiptText => ReadDimension(Access.Receipt, Summary?.ReceiptAvailability, () => !Summary!.IsActive ? "不适用（非活动）" : Summary.ReceiptStatus == "None" ? "无关联采购行" : $"整行收足 {TrackingSummaryText.Count(Summary.ReceiptCompletedLineCount)} / 未收足 {TrackingSummaryText.Count(Summary.ReceiptOutstandingLineCount)} / 超收 {TrackingSummaryText.Count(Summary.ReceiptExcessLineCount)} / 核实 {TrackingSummaryText.Count(Summary.ReceiptReviewLineCount)}");
}
