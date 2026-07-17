using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public sealed class NetDemandDialogService(
    PurchaseDraftPreviewViewModel previewViewModel,
    IContentDialogService contentDialogService) : INetDemandDialogService
{
    public async Task<bool> ShowPurchaseDraftPreviewAsync(
        PurchaseDraftPreviewOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        await previewViewModel.OpenAsync(request, cancellationToken);
        var view = new PurchaseDraftPreviewView { DataContext = previewViewModel };
        var dialog = new ContentDialog
        {
            Title = "转采购草稿预览",
            Content = view,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Closing += (_, args) =>
        {
            // 创建接口可能已经进入数据库事务；此时不允许关闭并取消客户端等待，
            // 否则服务端提交成功后工作台不会刷新，也看不到逐来源执行结果。
            if (!previewViewModel.CanClose)
            {
                args.Cancel = true;
            }
        };

        await contentDialogService.ShowAsync(dialog, cancellationToken);
        var hasAppliedResults = previewViewModel.HasAppliedResults;
        previewViewModel.Close();
        return hasAppliedResults;
    }
}
