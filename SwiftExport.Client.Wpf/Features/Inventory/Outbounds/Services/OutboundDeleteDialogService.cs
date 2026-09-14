using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>组装出库专用删除弹窗，只有用户确认有效原因后才返回内容。</summary>
public sealed class OutboundDeleteDialogService(IDialogService dialogs) : IOutboundDeleteDialogService
{
    /// <inheritdoc/>
    public async Task<string?> ConfirmAsync(int count, CancellationToken token)
    {
        var model = new OutboundDeleteDialogViewModel();
        var result = await dialogs.ShowAsync(new OutboundDeleteDialogView { DataContext = model },
            new DialogOptions($"删除 {count} 张出库草稿")
            { PrimaryButtonText = "确认删除", CloseButtonText = "取消", Width = 480 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled ? model.Reason.Trim() : null;
    }
}
