using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public sealed class ProcurementDialogService(NetDemandPurchasePickerViewModel picker, IContentDialogService dialogs) : IProcurementDialogService
{
    public async Task<IReadOnlyList<NetDemandRowModel>> SelectNetDemandsAsync(CancellationToken cancellationToken = default)
    {
        await picker.OpenAsync(cancellationToken);
        var dialog = new ContentDialog { Title = "从净需求获取采购草稿", Content = new NetDemandPurchasePickerView { DataContext = picker }, PrimaryButtonText = "进入预览", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        var result = await dialogs.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary ? picker.SelectedRows : [];
    }
}
