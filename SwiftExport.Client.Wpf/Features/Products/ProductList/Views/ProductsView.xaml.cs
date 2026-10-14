using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using SwiftExportClient.Features.Products.ProductList.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>物料管理视图，负责原生表格编辑提交和搜索焦点桥接。</summary>
public partial class ProductsView : UserControl
{
    private ProductsViewModel? _viewModel;
    private DataGrid? _productGrid;

    /// <summary>初始化物料管理视图。</summary>
    public ProductsView()
    {
        InitializeComponent();
    }

    /// <summary>视图加载时桥接原生编辑事务，重复进入先解除旧引用。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();

    /// <summary>卸载时解除 ViewModel 对视觉树的引用，不操作任何业务草稿。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as ProductsViewModel;
        if (_viewModel is not null) _viewModel.CommitPendingEdits = CommitEdits;
    }

    private void DetachViewModel()
    {
        if (_viewModel?.CommitPendingEdits == CommitEdits) _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }

    /// <summary>使用实际加载的表格桥接编辑，避免跨抽屉内容命名作用域引用控件。</summary>
    private void OnProductGridLoaded(object sender, RoutedEventArgs e) => _productGrid = (DataGrid)sender;

    /// <summary>表格卸载后释放视觉树引用，下一次加载重新建立提交桥。</summary>
    private void OnProductGridUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_productGrid, sender)) _productGrid = null;
    }

    /// <summary>只提交物料列表的原生单元格和行；BOM 抽屉保持自身编辑上下文。</summary>
    private bool CommitEdits() => _productGrid is null
        || _productGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && _productGrid.CommitEdit(DataGridEditingUnit.Row, true);

    /// <summary>按 Ctrl+1 将焦点移到搜索框，不接管表格原生编辑键。</summary>
    private void Root_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D1)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();

            e.Handled = true;
        }
    }
}
