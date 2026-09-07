using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>中性产品管理视图，桥接原生编辑提交和分类 TreeView 选择。</summary>
public partial class ProductNeutralManagerView : UserControl
{
    private ProductNeutralManagerViewModel? _viewModel;

    /// <summary>初始化中性产品管理视图。</summary>
    public ProductNeutralManagerView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as ProductNeutralManagerViewModel;
        if (_viewModel is not null) _viewModel.CommitPendingEdits = CommitEdits;
    }

    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }

    /// <summary>提交原生单元格和行事务；转换失败时保留当前编辑态。</summary>
    private bool CommitEdits() =>
        NeutralGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && NeutralGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private async void OnCategorySelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductNeutralManagerViewModel viewModel
            && e.NewValue is ProductNeutralManagerCategoryFilterNode node)
            await viewModel.SelectCategoryAsync(node);
    }
}
