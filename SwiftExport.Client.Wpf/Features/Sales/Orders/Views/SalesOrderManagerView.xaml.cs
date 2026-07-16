using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.Sales.Orders.ViewModels;

namespace SwiftExportClient.Features.Sales.Orders.Views;

public partial class SalesOrderManagerView : UserControl
{
    public SalesOrderManagerView()
    {
        InitializeComponent();
    }

    private async void OrdersGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SalesOrderManagerViewModel viewModel)
        {
            await viewModel.LoadSelectedLinesCommand.ExecuteAsync(null);
        }
    }

    private async void OrdersGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SalesOrderManagerViewModel viewModel
            && viewModel.OpenSelectedCommand.CanExecute(null))
        {
            await viewModel.OpenSelectedCommand.ExecuteAsync(null);
        }
    }
}
