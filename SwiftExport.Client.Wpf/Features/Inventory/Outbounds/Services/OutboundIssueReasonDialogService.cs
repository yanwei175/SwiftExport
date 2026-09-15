using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>组装保存前的原因补填弹窗，不执行保存或修改调用方模型。</summary>
public sealed class OutboundIssueReasonDialogService(IDialogService dialogs) : IOutboundIssueReasonDialogService
{
    /// <inheritdoc/>
    public async Task<(DialogResult Result, string Reason)> CollectAsync(string documentNo, CancellationToken token)
    {
        var model = new OutboundIssueReasonViewModel(documentNo);
        var result = await dialogs.ShowAsync(new OutboundIssueReasonView { DataContext = model },
            new DialogOptions("补填出库原因") { PrimaryButtonText = "确认并继续保存", CloseButtonText = "取消保存", Width = 560 }, model, token);
        return (result, model.Reason);
    }
}
