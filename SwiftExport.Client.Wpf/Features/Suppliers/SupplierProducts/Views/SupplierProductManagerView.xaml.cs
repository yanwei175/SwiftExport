using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.Views;

/// <summary>供应商产品页面，只桥接原生编辑、权限和加载生命周期。</summary>
public partial class SupplierProductManagerView : UserControl
{
    private SupplierProductManagerViewModel? _viewModel;
    /// <summary>初始化实际管理页面。</summary>
    public SupplierProductManagerView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) AttachViewModel(); }
    /// <summary>只连接当前VM，重复进入先对称解除。</summary>
    private void AttachViewModel()
    {
        DetachViewModel(); _viewModel = DataContext as SupplierProductManagerViewModel;
        if (_viewModel is null) return;
        _viewModel.CommitPendingEdits = CommitEdits; _viewModel.PropertyChanged += OnPermissionsChanged;

    }
    /// <summary>释放本视图拥有的回调，避免VM持有卸载视觉树。</summary>
    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        if (_viewModel.CommitPendingEdits == CommitEdits) _viewModel.CommitPendingEdits = null;

        _viewModel.PropertyChanged -= OnPermissionsChanged; _viewModel = null;
    }
    /// <summary>编辑开始按真实行的新旧状态与归属复核权限。</summary>
    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    { if (e.Row.Item is not SupplierProductLineModel row || _viewModel?.CanEditRow(row) != true) e.Cancel = true; }
    /// <summary>撤权取消尚未提交的编辑器，已经形成的草稿仍由列表保留。</summary>
    private void OnPermissionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SupplierProductManagerViewModel.CanView) or nameof(SupplierProductManagerViewModel.CanCreate) or nameof(SupplierProductManagerViewModel.CanEdit) or nameof(SupplierProductManagerViewModel.CanWrite))
            if (ProductGrid.CurrentItem is SupplierProductLineModel row && _viewModel?.CanEditRow(row) != true)
            { ProductGrid.CancelEdit(DataGridEditingUnit.Cell); ProductGrid.CancelEdit(DataGridEditingUnit.Row); }
    }
    /// <summary>原生转换失败时保持输入缓冲，阻止业务操作继续。</summary>
    private bool CommitEdits() => ProductGrid.CommitEdit(DataGridEditingUnit.Cell, true) && ProductGrid.CommitEdit(DataGridEditingUnit.Row, true);

}
