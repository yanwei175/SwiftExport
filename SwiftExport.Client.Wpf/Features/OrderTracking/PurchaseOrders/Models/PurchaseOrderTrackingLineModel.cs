using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;

/// <summary>采购跟踪全量行的只读显示，不把货好数量当作正式收货，也不截断异常负数。</summary>
public sealed partial class PurchaseOrderTrackingLineModel(PurchaseOrderTrackingLineDto data, PurchaseOrderTrackingItemDto? order = null)
{
    /// <summary>服务端采购行快照、关联事实及独立进度维度。</summary>
    public PurchaseOrderTrackingLineDto Data { get; } = data;
    /// <summary>供应商未反馈数量保持未知。</summary>
    public string ReadyText => Data.CanViewProgress ? Data.ReadyQty?.ToString("0.####") ?? "尚未反馈" : "无权";
    /// <summary>供应商报告的预计完成，不覆盖采购行计划交期。</summary>
    public string CompletionText => Data.CanViewProgress ? PurchaseTrackingText.Date(Data.ExpectedCompletionDate) : "无权";
    /// <summary>最近反馈时间仅在反馈维度可见。</summary>
    public string FollowUpText => Data.CanViewProgress ? Data.LatestFollowUpDate?.ToString("yyyy-MM-dd HH:mm") ?? "未反馈" : "无权";
    /// <summary>反馈人员沿当前维度权限显示。</summary>
    public string FollowUpUserText => Data.CanViewProgress ? Data.FollowUpUserName ?? string.Empty : "无权";
    /// <summary>可信正式净收货数量。</summary>
    public string ReceivedText => !Data.CanViewReceipts ? "无权" : ReceiptNotApplicable ? "未确认，不计待收" : PurchaseTrackingText.Quantity(Data.ReceivedQty);
    /// <summary>服务端采购量减正式净收货量，负数与空值均原样保留。</summary>
    public string RemainingText => !Data.CanViewReceipts ? "无权" : ReceiptNotApplicable ? "未确认，不计待收" : PurchaseTrackingText.Quantity(Data.UnreceivedQty);
    /// <summary>沿父单的正式收货适用范围显示空值；异常证据有状态或原因，仍明确待核实。</summary>
    private bool ReceiptNotApplicable => order?.ConfirmationStatus == ConfirmationStatusEnum.待确认 && order.ReceiptStatus is null
        && Data.ReceivedQty is null && Data.UnreceivedQty is null && string.IsNullOrWhiteSpace(Data.ReceiptReviewReason);
    /// <summary>贡献关联的未知与作废不适用分别显示。</summary>
    public string LinkedText => Data.AssociationStatus == PurchaseDemandAssociationStatusEnum.NotApplicable ? "不适用" : PurchaseTrackingText.Quantity(Data.LinkedDemandQty);
    /// <summary>未关联量不是未收货数量。</summary>
    public string UnlinkedText => Data.AssociationStatus == PurchaseDemandAssociationStatusEnum.NotApplicable ? "不适用" : PurchaseTrackingText.Quantity(Data.UnlinkedQty);
}
