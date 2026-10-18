using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;

namespace SwiftExportClient.Features.Suppliers.SupplierManagement.Views;

/// <summary>供应商页面，只桥接原生编辑、权限和列筛选生命周期。</summary>
public partial class SupplierManagerView : UserControl
{
    private SupplierManagerViewModel? _viewModel;
    /// <summary>初始化实际管理页面。</summary>
    public SupplierManagerView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) AttachViewModel(); }
    /// <summary>只连接当前VM，重复进入先对称解除。</summary>
    private void AttachViewModel()
    {
        DetachViewModel(); _viewModel = DataContext as SupplierManagerViewModel;
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
    { if (e.Row.Item is not SupplierLineModel row || _viewModel?.CanEditRow(row) != true) e.Cancel = true; }
    /// <summary>撤权取消尚未提交的编辑器，已经形成的草稿仍由列表保留。</summary>
    private void OnPermissionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SupplierManagerViewModel.CanView) or nameof(SupplierManagerViewModel.CanCreate) or nameof(SupplierManagerViewModel.CanEdit) or nameof(SupplierManagerViewModel.CanWrite))
            if (SupplierGrid.CurrentItem is SupplierLineModel row && _viewModel?.CanEditRow(row) != true)
            { SupplierGrid.CancelEdit(DataGridEditingUnit.Cell); SupplierGrid.CancelEdit(DataGridEditingUnit.Row); }
    }
    /// <summary>原生转换失败时保持输入缓冲，阻止业务操作继续。</summary>
    private bool CommitEdits() => SupplierGrid.CommitEdit(DataGridEditingUnit.Cell, true) && SupplierGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
