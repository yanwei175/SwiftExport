using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;

/// <summary>客户采购默认配置视图，桥接原生 DataGrid 编辑事务。</summary>
public partial class CustomerPoConfigManagerView : UserControl
{
    private CustomerPoConfigManagerViewModel? _viewModel;

    /// <summary>初始化客户采购默认配置视图。</summary>
    public CustomerPoConfigManagerView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }
    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as CustomerPoConfigManagerViewModel;
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
        ConfigGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && ConfigGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
