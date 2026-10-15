using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.PackagingMaterials.ViewModels;

namespace SwiftExportClient.Features.Products.PackagingMaterials.Views;

/// <summary>包材管理原生表格的视觉桥，只提交单元格/行事务及复核编辑权限，不编排业务保存。</summary>
public partial class PackagingMaterialManagerView : UserControl
{
    private PackagingMaterialManagerViewModel? _viewModel;
    /// <summary>初始化包材管理视图。</summary>
    public PackagingMaterialManagerView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) { if (IsLoaded) Attach(); }
    /// <summary>重复加载时只保留当前页面的编辑提交桥。</summary>
    private void Attach() { Detach(); _viewModel = DataContext as PackagingMaterialManagerViewModel; if (_viewModel is not null) _viewModel.CommitPendingEdits = CommitEdits; }
    private void Detach() { if (_viewModel?.CommitPendingEdits == CommitEdits) _viewModel.CommitPendingEdits = null; _viewModel = null; }
    /// <summary>转换验证失败时保持原生编辑缓冲，由 ViewModel 阻断保存或导航。</summary>
    private bool CommitEdits() => PackageGrid.CommitEdit(DataGridEditingUnit.Cell, true) && PackageGrid.CommitEdit(DataGridEditingUnit.Row, true);
    /// <summary>勾选只改变 UI 选择；资料编辑按行级创建/修改权限复核，只有删除权限时仍可勾选多行。</summary>
    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs args)
    {
        if (_viewModel is null || _viewModel.IsGridReadOnly) { args.Cancel = true; return; }
        if (args.Column is DataGridCheckBoxColumn) return;
        if (args.Row.Item is not ProductLineModel row || !_viewModel.CanEditRow(row)) args.Cancel = true;
    }
}
