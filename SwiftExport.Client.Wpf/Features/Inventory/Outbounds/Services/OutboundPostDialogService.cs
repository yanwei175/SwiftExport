using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>组装出库过账预览弹窗，不持有 Manager 或发送库存请求。</summary>
public sealed class OutboundPostDialogService(IDialogService dialogs) : IOutboundPostDialogService
{
    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(OutboundPostPreviewDto preview, CancellationToken token)
    {
        var model = new OutboundPostPreviewViewModel(preview);
        var result = await dialogs.ShowAsync(new OutboundPostPreviewView { DataContext = model },
            new DialogOptions("出库过账预览") { PrimaryButtonText = "确认过账", CloseButtonText = "取消", Width = 1000 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled;
    }
}
