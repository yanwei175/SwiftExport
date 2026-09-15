using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Inventory.Inbounds.Services;
/// <summary>组装反向预览和原因弹窗，只返回确认结果。</summary>
public sealed class InboundReverseDialogService(IDialogService dialogs) : IInboundReverseDialogService
{
    /// <inheritdoc/>
    public async Task<string?> ConfirmAsync(InboundReversePreviewDto preview, CancellationToken token)
    {
        var model = new InboundReversePreviewViewModel(preview);
        var result = await dialogs.ShowAsync(new InboundReversePreviewView { DataContext = model },
            new DialogOptions("手工入库整单反过账") { PrimaryButtonText = "确认整单反过账", CloseButtonText = "取消", Width = 1000 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled ? model.Reason.Trim() : null;
    }
}
