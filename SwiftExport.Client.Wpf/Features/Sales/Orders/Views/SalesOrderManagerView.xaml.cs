using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.Sales.Orders.ViewModels;

namespace SwiftExportClient.Features.Sales.Orders.Views;

/// <summary>承载销售订单管理交互，并在保活页签隐藏时关闭页面级筛选浮层。</summary>
public partial class SalesOrderManagerView : UserControl
{
    /// <summary>初始化销售订单管理视图及其可见性生命周期处理。</summary>
    public SalesOrderManagerView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>页签隐藏时关闭脱离主视觉树的高级筛选 Popup，筛选值保持不变。</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && DataContext is SalesOrderManagerViewModel viewModel)
        {
            viewModel.IsAdvancedFilterOpen = false;
        }
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
