using System.Globalization;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExportClient.Resources.Converters;

namespace SwiftExportClient.Features.OrderTracking.DemandDetail.Views;

/// <summary>把跟踪证据状态翻译成中文，采购目标缺失时不显示默认枚举造成的虚假状态。</summary>
public sealed class TrackingEvidenceTextConverter : IValueConverter
{
    /// <summary>根据实际目标和历史证据显示状态，不推断未知数量。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var kind = parameter as string;
        if (value is DemandTrackingReceiptDto receipt)
        {
            if (receipt.CountsTowardReceipt is null || !string.IsNullOrWhiteSpace(receipt.ReviewReason)) return "待核实";
            return new EnumDescriptionConverter().Convert(receipt.PostingStatus, typeof(string), null!, culture);
        }
        if (value is DemandTrackingPurchaseDto purchase)
        {
            if (!purchase.HasPurchaseTarget) return "目标缺失 / 待核实";
            object state = kind == "PurchaseLifecycle" ? purchase.LifecycleStatus : purchase.ConfirmationStatus;
            return new EnumDescriptionConverter().Convert(state, typeof(string), null!, culture);
        }
        if (value is DemandTrackingDeliveryDto delivery)
        {
            if (delivery.ReversedDate is not null) return "反向历史（不计入）";
            if (!string.IsNullOrWhiteSpace(delivery.ReviewReason)) return "待核实";
            return delivery.CountsTowardDelivery ? "正式交付（有效已过账）" : "未计入正式交付";
        }
        return kind switch
        {
            "Receipt" => value is true ? "计入采购整行实收" : value is false ? "未计入（草稿或反向）" : "待核实",
            "Deleted" => value is true ? "已删除历史" : "未删除",
            "ActiveLink" => value is true ? "活动关联" : "历史关联",
            "Shared" => value is true ? "共享采购行" : "非共享采购行",
            "Overdue" => value is true ? "逾期" : value is false ? "未逾期" : "待核实",
            _ => value?.ToString() ?? "待核实"
        };
    }

    /// <summary>只读证据显示不支持反向写入。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
