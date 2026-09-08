namespace SwiftExportClient.Features.QualityControl.AfterSales.Services;

/// <summary>采集售后取消确认及生命周期命令的必填原因。</summary>
public interface IAfterSalesReasonDialogService
{
    /// <summary>显示原因输入框；取消返回 null，确认返回清理后的文本。</summary>
    Task<string?> RequestAsync(string title, string guidance, CancellationToken cancellationToken = default);
}
