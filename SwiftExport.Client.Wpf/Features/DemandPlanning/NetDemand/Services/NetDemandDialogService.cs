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
    /// <summary>展示物料领域候选并只把用户显式勾选的来源需求带回工作台。</summary>
    public async Task<IReadOnlyList<SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.SourceDemandCandidateDto>> ShowSourceDemandCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        await sourceDemandPicker.OpenAsync(cancellationToken);
        var dialog = new ContentDialog
        {
            Title = "从来源需求建立物料需求",
            Content = new SourceDemandCandidatePickerView { DataContext = sourceDemandPicker },
            PrimaryButtonText = "建立物料需求",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            DialogWidth = 1100,
            DialogMaxWidth = 1100
        };
        var result = await contentDialogService.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary ? sourceDemandPicker.SelectedRows : [];
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
