using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using SwiftExportClient.Features.Products.Attributes.ViewModels;

namespace SwiftExportClient.Features.Products.Attributes.Views;

/// <summary>独立标签维护视图，仅桥接 WPF 编辑提交、弹出层取消及加载生命周期。</summary>
public partial class ProductAttributeManagerView : UserControl
{
    private readonly Func<bool> _commitCallback;
    private ProductAttributeManagerViewModel? _viewModel;
    /// <summary>初始化独立维护页，数据上下文由工作区导航提供。</summary>
    public ProductAttributeManagerView()
    { InitializeComponent(); _commitCallback = CommitPendingEdits; }

    /// <summary>提交两张表当前编辑；转换错误时保留编辑并阻止触发替换上下文或保存。</summary>
    public bool CommitPendingEdits()
    {
        if (!CommitGrid(GroupGrid) || !CommitGrid(OptionGrid)) return false;
        CloseCategoryPopup();
        return true;
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs args) { CloseCategoryPopup(); DetachViewModel(); }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    { DetachViewModel(); if (IsLoaded) AttachViewModel(); }

    /// <summary>只在视图已加载时提供原生编辑回调，模型自身不读取视觉树。</summary>
    private void AttachViewModel()
    {
        if (DataContext is not ProductAttributeManagerViewModel viewModel || ReferenceEquals(_viewModel, viewModel)) return;
        DetachViewModel(); _viewModel = viewModel;
        viewModel.CommitPendingEdits = _commitCallback;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is not { } viewModel) return;
        if (ReferenceEquals(viewModel.CommitPendingEdits, _commitCallback)) viewModel.CommitPendingEdits = null;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    /// <summary>组选中身份或编辑能力变化时丢弃未确定的勾选，防止弹出层提交到另一组。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProductAttributeManagerViewModel.SelectedGroup)
            || args.PropertyName == nameof(ProductAttributeManagerViewModel.CanEditCurrentGroup) && _viewModel?.CanEditCurrentGroup != true)
            CloseCategoryPopup();
    }

    private void CloseCategoryPopup()
    { if (CategoryPicker.FindName("PART_Popup") is Popup popup) popup.IsOpen = false; }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        if (FindAncestor<ButtonBase>(source) is { Command: not null } button)
        {
            if (PrepareButtonAction(button)) return;
            args.Handled = true; return;
        }
        if (!CanMoveBetweenGrids(FindAncestor<DataGrid>(source))) args.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is Key.Enter or Key.Space && FindAncestor<ButtonBase>(args.OriginalSource as DependencyObject) is { Command: not null } button
            && !PrepareButtonAction(button)) args.Handled = true;
    }

    /// <summary>切组之前提交旧组选项，避免集合视图在原生编辑事务未结束时被刷新。</summary>
    private void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (!CanMoveBetweenGrids(FindAncestor<DataGrid>(args.NewFocus as DependencyObject))) args.Handled = true;
    }

    private bool CanMoveBetweenGrids(DataGrid? target) =>
        ReferenceEquals(target, GroupGrid) ? CommitGrid(OptionGrid) :
        ReferenceEquals(target, OptionGrid) ? CommitGrid(GroupGrid) : CommitGrid(GroupGrid) && CommitGrid(OptionGrid);

    /// <summary>撤销按钮先取消无效单元格缓冲，其他操作必须先成功提交当前编辑。</summary>
    private bool PrepareButtonAction(ButtonBase button)
    {
        if (DataContext is ProductAttributeManagerViewModel viewModel && ReferenceEquals(button.Command, viewModel.RejectCommand))
        {
            GroupGrid.CancelEdit(DataGridEditingUnit.Cell); GroupGrid.CancelEdit(DataGridEditingUnit.Row);
            OptionGrid.CancelEdit(DataGridEditingUnit.Cell); OptionGrid.CancelEdit(DataGridEditingUnit.Row);
            CloseCategoryPopup();
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

