using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public sealed class NetDemandDialogService(
    PurchaseDraftPreviewViewModel previewViewModel,
    SourceDemandCandidatePickerViewModel sourceDemandPicker,
    IContentDialogService contentDialogService) : INetDemandDialogService
{
    /// <summary>展示物料领域候选，建立成功后在对话框内刷新来源单与当前明细。</summary>
    public async Task<bool> ShowSourceDemandCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        await sourceDemandPicker.OpenAsync(cancellationToken);
        var dialog = new ContentDialog
        {
            Title = "从来源需求建立物料需求",
            Content = new SourceDemandCandidatePickerView { DataContext = sourceDemandPicker },
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            DialogWidth = 1400,
            DialogMaxWidth = 1600
        };
        dialog.Closing += (_, args) =>
        {
            // 写命令已经进入服务端后不允许关闭并丢失结果接纳与刷新。
            if (sourceDemandPicker.IsWriting)
            {
                args.Cancel = true;
            }
        };
        try
        {
            await contentDialogService.ShowAsync(dialog, cancellationToken);
            return sourceDemandPicker.HasEstablished;
        }
        finally
        {
            sourceDemandPicker.Close();
        }
    }

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
            DialogMaxWidth = 1980,
            DialogWidth = 1980,
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
