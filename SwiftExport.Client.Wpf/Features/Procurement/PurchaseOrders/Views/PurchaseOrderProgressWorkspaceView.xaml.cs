using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>供应商反馈选单与订单录入视图，将真实行交互转发给同一反馈文档能力。</summary>
public partial class PurchaseOrderProgressWorkspaceView : UserControl
{
    /// <summary>初始化视图绑定及原生表格事件。</summary>
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

    /// <summary>只响应本表真实采购行，列头、滚动条及空白双击不得沿用旧选中行打开反馈。</summary>
    private async void LinesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(grid, source) is DataGridRow row
            && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), grid)
            && DataContext is PurchaseOrderProgressWorkspaceViewModel viewModel
            && ReferenceEquals(row.Item, viewModel.SelectedLine)
            && viewModel.OpenDrawerCommand.CanExecute(null))
        {
            await viewModel.OpenDrawerCommand.ExecuteAsync(null);
        }
    }
}
