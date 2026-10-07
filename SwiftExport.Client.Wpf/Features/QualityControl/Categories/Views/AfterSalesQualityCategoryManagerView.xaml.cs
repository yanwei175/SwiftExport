using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GongSolutions.Wpf.DragDrop;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Features.QualityControl.Categories.ViewModels;

namespace SwiftExportClient.Features.QualityControl.Categories.Views;

/// <summary>分类视图只桥接选择、输入转换与 WPF 拖动位置，层级变更交由 ViewModel。</summary>
public partial class AfterSalesQualityCategoryManagerView : UserControl, IDropTarget
{
    private AfterSalesQualityCategoryManagerViewModel? _attached;
    /// <summary>创建界面，ViewModel 由导航组合根提供。</summary>
    public AfterSalesQualityCategoryManagerView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _attached = DataContext as AfterSalesQualityCategoryManagerViewModel;
        if (_attached is not null) _attached.CommitPendingEdits = CommitEdits;
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_attached is not null) _attached.CommitPendingEdits = null;
        _attached = null;
    }
    /// <summary>提交当前输入并阻止无效数字静默回退为旧值。</summary>
    private bool CommitEdits()
    {
        foreach (var textBox in Editor.Children.OfType<TextBox>())
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(textBox)) { textBox.Focus(); return false; }
        }
        return true;
    }
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is AfterSalesQualityCategoryManagerViewModel vm) vm.CurrentCategory = e.NewValue as AfterSalesQualityCategoryNode;
    }
    /// <summary>右键上下文菜单操作鼠标所指节点，不沿用先前选中项。</summary>
    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindItem(e.OriginalSource as DependencyObject);
        if (item is not null) { item.IsSelected = true; item.Focus(); }
    }
    private static TreeViewItem? FindItem(DependencyObject? value)
    {
        while (value is not null && value is not TreeViewItem) value = VisualTreeHelper.GetParent(value);
        return value as TreeViewItem;
    }
    /// <summary>按目标项上中下区域区分插入同级或成为子级。</summary>
    private static RelativeInsertPosition Position(IDropInfo info)
    {
        if (info.VisualTargetItem is FrameworkElement item && info.VisualTarget is UIElement tree)
        {
            var y = tree.TranslatePoint(info.DropPosition, item).Y;
            return y < item.ActualHeight * .25 ? RelativeInsertPosition.BeforeTargetItem
                : y > item.ActualHeight * .75 ? RelativeInsertPosition.AfterTargetItem : RelativeInsertPosition.TargetItemCenter;
        }
        return info.InsertPosition;
    }
    /// <summary>把视觉位置转换为模型父级和插入索引，不直接修改业务树。</summary>
    private static (Guid? Parent, int Index) Destination(AfterSalesQualityCategoryManagerViewModel vm, IDropInfo info)
    {
        if (info.TargetItem is not AfterSalesQualityCategoryNode target) return (null, vm.Tree.Roots.Count);
        var position = Position(info);
        return position == RelativeInsertPosition.TargetItemCenter
            ? (target.Uid, target.Children.Count)
            : (target.ParentId, vm.Tree.Siblings(target.ParentId).IndexOf(target) + (position == RelativeInsertPosition.AfterTargetItem ? 1 : 0));
    }
    /// <inheritdoc/>
    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        dropInfo.Effects = DragDropEffects.None;
        if (DataContext is not AfterSalesQualityCategoryManagerViewModel vm || !vm.CanEdit
            || dropInfo.Data is not AfterSalesQualityCategoryNode node || ReferenceEquals(node, dropInfo.TargetItem)) return;
        var destination = Destination(vm, dropInfo);
        if (!vm.Tree.CanMove(node, destination.Parent)) return;
        dropInfo.Effects = DragDropEffects.Move;
        dropInfo.DropTargetAdorner = Position(dropInfo) == RelativeInsertPosition.TargetItemCenter && dropInfo.TargetItem is not null
            ? DropTargetAdorners.Highlight : DropTargetAdorners.Insert;
    }
    /// <inheritdoc/>
    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        if (DataContext is not AfterSalesQualityCategoryManagerViewModel vm || dropInfo.Data is not AfterSalesQualityCategoryNode node
            || ReferenceEquals(node, dropInfo.TargetItem)) return;
        var destination = Destination(vm, dropInfo);
        vm.Move(node, destination.Parent, destination.Index);
    }
    /// <inheritdoc/>
    void IDropTarget.DragEnter(IDropInfo dropInfo) { }
    /// <inheritdoc/>
    void IDropTarget.DragLeave(IDropInfo dropInfo) { }
    /// <inheritdoc/>
    public void DropHint(IDropHintInfo dropHintInfo) { }
}
