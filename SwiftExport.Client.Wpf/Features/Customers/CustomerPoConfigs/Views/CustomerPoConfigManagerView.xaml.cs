using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;

/// <summary>客户采购默认配置视图，桥接原生 DataGrid 编辑事务。</summary>
public partial class CustomerPoConfigManagerView : UserControl
{
    private CustomerPoConfigManagerViewModel? _viewModel;

    /// <summary>初始化客户采购默认配置视图。</summary>
    public CustomerPoConfigManagerView() => InitializeComponent();

    /// <summary>进入页面时连接原生提交与运行时权限通知。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    /// <summary>离开页面时释放 VM 对视觉树的引用。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();
    /// <summary>已加载页面更换数据上下文时对称重接。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }
    /// <summary>只桥接当前页面的编辑事务，不承载权限规则。</summary>
    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as CustomerPoConfigManagerViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CommitPendingEdits = CommitEdits;
            _viewModel.PropertyChanged += OnPermissionsChanged;
        }
    }
    /// <summary>释放当前页面拥有的回调及订阅，避免重载重复挂接。</summary>
    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnPermissionsChanged;
        if (_viewModel.CommitPendingEdits == CommitEdits) _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }
    /// <summary>按真实行的新建状态及所属页面复核编辑许可。</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not CustomerPoConfigLineModel row || _viewModel?.CanEditRow(row) != true) e.Cancel = true;
    }

    /// <summary>撤权时取消尚未提交的编辑器，保留已经形成的列表草稿。</summary>
    private void OnPermissionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CustomerPoConfigManagerViewModel.CanView) or nameof(CustomerPoConfigManagerViewModel.CanCreate)
            or nameof(CustomerPoConfigManagerViewModel.CanEdit) or nameof(CustomerPoConfigManagerViewModel.CanWrite))
            if (ConfigGrid.CurrentItem is CustomerPoConfigLineModel row && _viewModel?.CanEditRow(row) != true)
            { ConfigGrid.CancelEdit(DataGridEditingUnit.Cell); ConfigGrid.CancelEdit(DataGridEditingUnit.Row); }
    }

    /// <summary>提交原生单元格和行事务；转换失败时保留当前编辑态。</summary>
    private bool CommitEdits() =>
        ConfigGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && ConfigGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
