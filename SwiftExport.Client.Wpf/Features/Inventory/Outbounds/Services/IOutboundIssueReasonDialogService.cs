using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>采集单张草稿的出库原因，按钮结果明确区分确认和关闭。</summary>
public interface IOutboundIssueReasonDialogService
{
    /// <summary>显示单据标识并返回本次原因；只有 Primary 结果可以接纳到草稿。</summary>
    Task<(DialogResult Result, string Reason)> CollectAsync(string documentNo, CancellationToken token);
}
