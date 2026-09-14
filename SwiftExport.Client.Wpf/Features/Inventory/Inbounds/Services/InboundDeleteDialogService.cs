using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>组装入库专用删除弹窗，只有用户确认有效原因后才返回内容。</summary>
public sealed class InboundDeleteDialogService(IDialogService dialogs) : IInboundDeleteDialogService
{
    /// <inheritdoc/>
    public async Task<string?> ConfirmAsync(int count, CancellationToken token)
    {
        var model = new InboundDeleteDialogViewModel();
        var result = await dialogs.ShowAsync(new InboundDeleteDialogView { DataContext = model },
            new DialogOptions($"删除 {count} 张入库草稿")
            { PrimaryButtonText = "确认删除", CloseButtonText = "取消", Width = 480 }, model, token);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled ? model.Reason.Trim() : null;
    }
}
