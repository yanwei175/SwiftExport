using System.ComponentModel;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;

/// <summary>包装服务端采购跟踪事实，只格式化权限和未知值，不在客户端重算货好或收货状态。</summary>
public sealed partial class PurchaseOrderTrackingRowModel(PurchaseOrderTrackingItemDto data)
{
    /// <summary>采购头基础事实与服务端摘要，保持原单位和可空数量。</summary>
    public PurchaseOrderTrackingItemDto Data { get; } = data;
    /// <summary>创建类别不因后来补关联而改变。</summary>
    public string OriginText => Data.OriginKind switch { 1 => "需求创建", 2 => "独立创建", _ => "需核实" };
    /// <summary>无反馈权限与未知状态分别显示。</summary>
    public string ReadyText => Data.CanViewProgress ? PurchaseTrackingText.Status(Data.ReadyStatus) : "无权";
    /// <summary>正式收货状态不从供应商累计货好量推导。</summary>
    public string ReceiptText => !Data.CanViewReceipts ? "无权" : ReceiptNotApplicable ? "未确认，不计待收" : PurchaseTrackingText.Status(Data.ReceiptStatus);
    /// <summary>货好量未知不能当作零。</summary>
    public string ReadyQtyText => Data.CanViewProgress ? PurchaseTrackingText.Quantity(Data.ReadyQty) : "无权";
    /// <summary>仅展示服务端同单位可信正式净收货比例。</summary>
    public string ReceiptProgressText => !Data.CanViewReceipts ? "无权" : ReceiptNotApplicable ? "未确认，不计待收" : Data.ReceiptProgress?.ToString("P0") ?? "需核实";
    /// <summary>服务端将正常未确认草稿的正式收货维度置空；证据异常则明确返回需核实状态。</summary>
    private bool ReceiptNotApplicable => Data.ConfirmationStatus == ConfirmationStatusEnum.待确认 && Data.ReceiptStatus is null;
    /// <summary>反馈预计完成与订单约定交期分别表达。</summary>
    public string CompletionText => Data.CanViewProgress ? PurchaseTrackingText.Date(Data.ExpectedCompletionDate) : "无权";
    /// <summary>无权维度不暴露计数。</summary>
    public string ReportedCountText => Data.CanViewProgress ? Data.ReportedLineCount?.ToString() ?? "需核实" : "无权";
    /// <summary>交期风险计数保留未知。</summary>
    public string DueHitText => Data.CanViewProgress ? Data.DueHitLineCount?.ToString() ?? "需核实" : "无权";
    /// <summary>交期命中行的货好量尚未反馈，不等同于采购单确认状态。</summary>
    public string RiskText => !Data.CanViewProgress ? "无权" : Data.HasUnconfirmedReadyRisk switch { true => "命中行货好量尚未反馈", false => "无", _ => "需核实" };
}

/// <summary>本采购跟踪切片的显示格式；不参与业务计算或条件判断。</summary>
internal static class PurchaseTrackingText
{
    internal static string Quantity(decimal? value) => value?.ToString("0.####") ?? "需核实";
    internal static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "未反馈";
    internal static string Status(Enum? value)
    {
        if (value is null) return "需核实";
        var field = value.GetType().GetField(value.ToString());
        return field is null ? "未知状态" : field.GetCustomAttributes(typeof(DescriptionAttribute), false).OfType<DescriptionAttribute>().FirstOrDefault()?.Description ?? value.ToString();
    }
}
