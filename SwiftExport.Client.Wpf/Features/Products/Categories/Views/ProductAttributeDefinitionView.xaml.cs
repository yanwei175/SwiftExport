using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Products.Categories.ViewModels;

namespace SwiftExportClient.Features.Products.Categories.Views;

/// <summary>标签定义两表视图，仅负责当前 WPF 单元格编辑提交的视觉桥接。</summary>
public partial class ProductAttributeDefinitionView : UserControl
{
    /// <summary>初始化标签定义视图，数据上下文由分类宿主提供。</summary>
    public ProductAttributeDefinitionView() => InitializeComponent();

    /// <summary>提交两张表当前编辑；转换错误时保留编辑并阻止触发替换上下文或保存。</summary>
    public bool CommitPendingEdits() =>
        CommitGrid(GroupGrid) && CommitGrid(OptionGrid);

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        if (FindAncestor<ButtonBase>(source) is { } button)
        {
            if (PrepareButtonAction(button)) return;
            args.Handled = true; return;
        }
        if (!CanMoveBetweenGrids(FindAncestor<DataGrid>(source))) args.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.Enter or Key.Space && FindAncestor<ButtonBase>(args.OriginalSource as DependencyObject) is { } button
            && !PrepareButtonAction(button)) args.Handled = true;
    }

    /// <summary>切组之前提交旧组选项，避免集合视图在原生编辑事务未结束时被刷新。</summary>
    private void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (!CanMoveBetweenGrids(FindAncestor<DataGrid>(args.NewFocus as DependencyObject))) args.Handled = true;
    }

    private bool CanMoveBetweenGrids(DataGrid? target) =>
        ReferenceEquals(target, GroupGrid) ? CommitGrid(OptionGrid) :
        !ReferenceEquals(target, OptionGrid) || CommitGrid(GroupGrid);

    /// <summary>撤销按钮先取消无效单元格缓冲，其他操作必须先成功提交当前编辑。</summary>
    private bool PrepareButtonAction(ButtonBase button)
    {
        if (DataContext is ProductAttributeDefinitionViewModel viewModel && ReferenceEquals(button.Command, viewModel.RejectCommand))
        {
            GroupGrid.CancelEdit(DataGridEditingUnit.Cell); GroupGrid.CancelEdit(DataGridEditingUnit.Row);
            OptionGrid.CancelEdit(DataGridEditingUnit.Cell); OptionGrid.CancelEdit(DataGridEditingUnit.Row);
            return true;
        }
        return CommitPendingEdits();
    }

    private static bool CommitGrid(DataGrid grid) =>
        grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);

    private static T? FindAncestor<T>(DependencyObject? item) where T : DependencyObject
    {
        while (item is not null)
        {
            if (item is T match) return match;
            item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        }
        return null;
    }
}

