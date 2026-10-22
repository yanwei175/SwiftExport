using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views;

/// <summary>桥接两表原生编辑、选择及手柄提交；包装业务仍由 ViewModel 执行。</summary>
public partial class PackingListDetailView : UserControl
{
    private PackingListDetailViewModel? _owner;
    private bool _selecting;
    private bool _committing;

    /// <summary>接入原生编辑提交与生命周期，视图卸载后不保留回调。</summary>
    public PackingListDetailView()
    {
        InitializeComponent();
        RowDragReorder.SetCommitEdits(OrderLinesGrid, CommitEditors);
        RowDragReorder.SetCommitEdits(PackingMembersGrid, CommitEditors);
        OrderLinesGrid.BeginningEdit += (_, e) =>
        {
            // 汇总行只有整箱模板列可编辑，不能编辑不存在的产品对象。
            if (e.Row.Item is PackingListPackingGroupModel { IsMixed: true }
                && e.Column is DataGridBoundColumn) e.Cancel = true;
        };
        Loaded += (_, _) => AttachCommit();
        Unloaded += (_, _) => DetachCommit();
        DataContextChanged += (_, _) => { DetachCommit(); if (IsLoaded) AttachCommit(); };
    }

    /// <summary>切箱前提交两表当前输入；失败保留原目标，成功后同步多选快照。</summary>
    private void OnPackageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting || _committing || DataContext is not PackingListDetailViewModel vm) return;
        _selecting = true;
        try
        {
            var binding = OrderLinesGrid.GetBindingExpression(DataGrid.SelectedItemProperty);
            if (!ReferenceEquals(OrderLinesGrid.SelectedItem, vm.SelectedPackage))
            {
                if (CommitEditors()) binding?.UpdateSource();
                else binding?.UpdateTarget();
            }
            vm.UpdatePackageSelection(OrderLinesGrid.SelectedItems.Cast<PackingListPackingGroupModel>().ToArray());
        }
        finally { _selecting = false; }
    }

    /// <summary>右键定位未选中的行；已多选的行保持选择，空白区域不误操作旧行。</summary>
    private void OnGridRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not DataGridRow && !ReferenceEquals(node, grid))
            node = VisualTreeHelper.GetParent(node);
        if (node is not DataGridRow row) { e.Handled = true; return; }
        if (!row.IsSelected)
        {
            if (!CommitEditors()) { e.Handled = true; return; }
            grid.SelectedItem = row.Item;
        }
        row.Focus();
    }

    /// <summary>只将当前加载视图的提交能力提供给页面。</summary>
    private void AttachCommit()
    {
        _owner = DataContext as PackingListDetailViewModel;
        if (_owner is not null) _owner.CommitEdits = CommitEditors;
    }
    /// <summary>解除回调，避免页面持有卸载的视觉树。</summary>
    private void DetachCommit() { if (_owner is not null) _owner.CommitEdits = null; _owner = null; }

    /// <summary>保存、切箱和拖动都先提交两张表，格式错误保留原始输入。</summary>
    private bool CommitEditors()
    {
        if (_committing) return false;
        _committing = true;
        try
        {
            CommitPackingEditors(this);
            return OrderLinesGrid.CommitEdit(DataGridEditingUnit.Cell, true)
                && OrderLinesGrid.CommitEdit(DataGridEditingUnit.Row, true)
                && PackingMembersGrid.CommitEdit(DataGridEditingUnit.Cell, true)
                && PackingMembersGrid.CommitEdit(DataGridEditingUnit.Row, true)
                && !PackingListInputValidation.HasError(this);
        }
        finally { _committing = false; }
    }

    /// <summary>模板编辑器显式回写，保证工具栏保存不遗漏仍聚焦的整箱指标。</summary>
    private static void CommitPackingEditors(DependencyObject parent)
    {
        if (parent is TextBox text && System.Windows.Automation.AutomationProperties.GetName(text).StartsWith("拼箱", StringComparison.Ordinal))
            text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            CommitPackingEditors(VisualTreeHelper.GetChild(parent, i));
    }
}
