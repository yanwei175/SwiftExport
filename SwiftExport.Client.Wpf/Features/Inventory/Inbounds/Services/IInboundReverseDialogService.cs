using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
namespace SwiftExportClient.Features.Inventory.Inbounds.Services;
/// <summary>反过账确认弹窗边界；取消返回空，确认返回独立原因文本。</summary>
public interface IInboundReverseDialogService
{
    /// <summary>展示原事实和扣减预览，确认后返回原因，不发送库存命令。</summary>
    Task<string?> ConfirmAsync(InboundReversePreviewDto preview, CancellationToken token);
}
