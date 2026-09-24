using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>组装入库过账预览弹窗，不持有 Manager 或发送库存请求。</summary>
public sealed class InboundPostDialogService(IDialogService dialogs) : IInboundPostDialogService
{
    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(InboundPostPreviewDto preview, CancellationToken token)
    {
        var model = new InboundPostPreviewViewModel(preview);
        var result = await dialogs.ShowAsync(new InboundPostPreviewView { DataContext = model },
            new DialogOptions("入库过账预览") { PrimaryButtonText = "确认过账", CloseButtonText = "取消", Width = 1000 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled;
    }
}
