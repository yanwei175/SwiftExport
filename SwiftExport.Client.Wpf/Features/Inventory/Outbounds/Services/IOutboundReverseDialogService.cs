using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
namespace SwiftExportClient.Features.Inventory.Outbounds.Services;
/// <summary>反过账确认弹窗边界；取消返回空，确认返回独立原因文本。</summary>
public interface IOutboundReverseDialogService
{
    /// <summary>展示原事实和恢复预览，确认后返回原因，不发送库存命令。</summary>
    Task<string?> ConfirmAsync(OutboundReversePreviewDto preview, CancellationToken token);
}
