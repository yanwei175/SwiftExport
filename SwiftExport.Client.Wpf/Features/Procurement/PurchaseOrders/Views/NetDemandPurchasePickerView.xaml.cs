using System.Windows.Controls;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>承载采购候选来源导航与本地筛选列表。</summary>
public partial class NetDemandPurchasePickerView : UserControl
{
    public NetDemandPurchasePickerView() => InitializeComponent();

    /// <summary>把来源切换交给 ViewModel；取消切换时恢复原选中项。</summary>
    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not NetDemandPurchasePickerViewModel viewModel
            || sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is PurchaseCandidateSourceGroupModel group)
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        listBox.SelectedItem = viewModel.SelectedSourceGroup;
    }
}
