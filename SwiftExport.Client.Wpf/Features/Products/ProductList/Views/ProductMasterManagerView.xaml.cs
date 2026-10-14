using System.Windows.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>主产品管理视图；桥接原生编辑提交及延迟选择，不接管 DataGrid 编辑键盘行为。</summary>
public partial class ProductMasterManagerView : UserControl
{
    /// <summary>初始化主产品管理视图。</summary>
    public ProductMasterManagerView() => InitializeComponent();

    private ProductMasterManagerViewModel? _viewModel;
    private bool _selectionQueued;

    /// <summary>视图进入时连接编辑提交和选择通知，重复进入不重复订阅。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();

    /// <summary>视图离开时解除引用，避免 ViewModel 持有已卸载控件。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    /// <summary>仅在供应商单元格原生进入编辑时恢复当前行候选，不接管编辑按键或改写行资料。</summary>
    private void OnMasterPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (GridColumn.GetKey(e.Column) == "default-supplier" && e.Row.Item is ProductMasterLineModel line)
            _viewModel?.PrepareSupplierEditor(line);
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as ProductMasterManagerViewModel;
        if (_viewModel is null) return;
        _viewModel.CommitPendingEdits = CommitEdits;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        QueueSelection();
    }

    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }

    /// <summary>整行选择和模式切换延迟处理；格子选区不触发主从查询或反复清空明细。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductMasterManagerViewModel.IsCardView)
            or nameof(ProductMasterManagerViewModel.IsCellSelectionMode)
            || e.PropertyName == nameof(ProductMasterManagerViewModel.SelectedMaster)
                && _viewModel is { } vm && (vm.IsCardView || !vm.IsCellSelectionMode))
            QueueSelection();
    }

    private void QueueSelection()
    {
        if (_selectionQueued) return;
        _selectionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ProcessSelection));
    }

    /// <summary>Dispatcher 事件边界观察所有异常，避免选择查询成为未观察任务。</summary>
    private async void ProcessSelection()
    {
        _selectionQueued = false;
        var vm = _viewModel;
        if (vm is null || !IsLoaded) return;
        try
        {
            await vm.SelectMasterAsync(vm.SelectedMaster);
            if (ReferenceEquals(vm, _viewModel)) await vm.EnsureNeutralsLoadedAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError("主产品选择切换失败：{0}", exception);
        }
    }

    /// <summary>只提交现有原生编辑事务；转换失败交回 DataGrid，不强行移动活动单元格。</summary>
    private bool CommitEdits() =>
        NeutralGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && NeutralGrid.CommitEdit(DataGridEditingUnit.Row, true)
        && MasterGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && MasterGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
