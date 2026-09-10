using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>负责在 WPF-UI 对话框中承载采购候选选择流程及有限正文布局。</summary>
public sealed class ProcurementDialogService(NetDemandPurchasePickerViewModel picker, IContentDialogService dialogs) : IProcurementDialogService
{
    /// <summary>打开采购候选对话框，并让候选列表在有限正文区域内自行滚动。</summary>
    public async Task<IReadOnlyList<MaterialDemandWorkspaceRowModel>> SelectNetDemandsAsync(CancellationToken cancellationToken = default)
    {
        var view = new NetDemandPurchasePickerView
        {
            DataContext = picker
        };
        var dialog = new ContentDialog
        {
            Title = "从净需求获取采购草稿",
            Content = view,
            PrimaryButtonText = "进入预览",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            DialogMaxWidth = 1400,
            DialogMaxHeight = 850
        };
        // WPF-UI 模板会把这两个附加属性传给 PART_ContentScroll；Disabled 让正文接受有限测量，
        // 避免外层 ScrollViewer 与页面内 DataGrid 争夺滚动所有权。
        ScrollViewer.SetHorizontalScrollBarVisibility(dialog, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(dialog, ScrollBarVisibility.Disabled);
        try
        {
            await picker.OpenAsync(cancellationToken);
            var result = await dialogs.ShowAsync(dialog, cancellationToken);
            return result == ContentDialogResult.Primary ? picker.SelectedRows : [];
        }
        finally
        {
            picker.Close();
        }
    }
}
