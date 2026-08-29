using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>采购订单跟进页面，将采购头选择和行双击事件转发给页面命令。</summary>
public partial class PurchaseOrderProgressWorkspaceView : UserControl
{
    public PurchaseOrderProgressWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OrdersGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PurchaseOrderProgressWorkspaceViewModel viewModel
            && viewModel.LoadSelectedOrderCommand.CanExecute(null))
        {
            await viewModel.LoadSelectedOrderCommand.ExecuteAsync(null);
        }
    }

    private void LinesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PurchaseOrderProgressWorkspaceViewModel viewModel
            && viewModel.OpenDrawerCommand.CanExecute(null))
        {
            viewModel.OpenDrawerCommand.Execute(null);
        }
    }
}
