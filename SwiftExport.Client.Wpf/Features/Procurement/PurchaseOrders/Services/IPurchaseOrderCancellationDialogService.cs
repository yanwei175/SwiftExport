namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>采集采购管理页快速取消确认的必填原因，不承担命令提交。</summary>
public interface IPurchaseOrderCancellationDialogService
{
    /// <summary>显示原因输入对话框；用户返回时结果为 null，确认时返回已清理的原因。</summary>
    Task<string?> RequestReasonAsync(string orderNo, CancellationToken cancellationToken = default);
}
