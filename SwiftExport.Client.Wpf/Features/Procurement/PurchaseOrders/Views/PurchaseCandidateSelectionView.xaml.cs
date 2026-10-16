using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Components;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>承载采购候选来源导航与本地筛选列表。</summary>
public partial class PurchaseCandidateSelectionView : UserControl
{
    private PurchaseCandidateSelectionViewModel? _viewModel;

    /// <summary>初始化采购候选选择组件。</summary>
    public PurchaseCandidateSelectionView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as PurchaseCandidateSelectionViewModel;
        if (_viewModel is not null) _viewModel.CommitPendingEdits = CommitEdits;
    }

    private void DetachViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }

    /// <summary>提交当前原生 Cell/Row 编辑事务；转换失败时保留编辑器并阻止发送旧值。</summary>
    private bool CommitEdits() =>
        CandidateGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && CandidateGrid.CommitEdit(DataGridEditingUnit.Row, true);

    /// <summary>用与 Ctrl+D 相同的行级许可阻止禁用供应商单元格进入编辑态。</summary>
    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not PurchaseCandidateItemModel row) return;
        var canEdit = !ReferenceEquals(e.Column, PurchaseSupplierColumn) || row.CanEditPurchaseSupplier;
        if (!canEdit) e.Cancel = true;
    }

    /// <summary>原生 BeginEdit 生成模板后把焦点交给当前 Lookup，保留 DataGrid 的双击、F2 与 Esc 语义。</summary>
    private void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (!ReferenceEquals(e.Column, PurchaseSupplierColumn)) return;
        var editingElement = e.EditingElement;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FindVisualChild<PurchaseSupplierLookupTextBox>(editingElement)?.FocusEditor();
        }), DispatcherPriority.Input);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindVisualChild<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        }
        return null;
    }

    /// <summary>把来源切换交给 ViewModel；取消切换时恢复原选中项。</summary>
    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not PurchaseCandidateSelectionViewModel viewModel
            || sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is PurchaseCandidateSourceGroupModel group)
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        listBox.SelectedItem = viewModel.SelectedSourceGroup;
    }
}
