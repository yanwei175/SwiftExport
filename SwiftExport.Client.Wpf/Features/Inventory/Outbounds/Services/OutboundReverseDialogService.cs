using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Inventory.Outbounds.Services;
/// <summary>组装反向预览和原因弹窗，只返回确认结果。</summary>
public sealed class OutboundReverseDialogService(IDialogService dialogs) : IOutboundReverseDialogService
{
    /// <inheritdoc/>
    public async Task<string?> ConfirmAsync(OutboundReversePreviewDto preview, CancellationToken token)
    {
        var model = new OutboundReversePreviewViewModel(preview);
        var result = await dialogs.ShowAsync(new OutboundReversePreviewView { DataContext = model },
            new DialogOptions("手工出库整单反过账") { PrimaryButtonText = "确认整单反过账", CloseButtonText = "取消", Width = 1000 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled ? model.Reason : null;
    }
}
