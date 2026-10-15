using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GongSolutions.Wpf.DragDrop;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using GongDragDrop = GongSolutions.Wpf.DragDrop.DragDrop;

namespace SwiftExportClient.Features.Products.Colors.Views;

/// <summary>桥接颜色表格拖拽位置、键盘重排和原生编辑提交；集合和排序号由 ViewModel 维护。</summary>
public partial class ProductColorsView : UserControl, IDropTarget
{
    /// <summary>创建颜色维护视图并安装只允许从行首手柄发起的单行拖拽源。</summary>
    public ProductColorsView()
    {
        InitializeComponent();
        GongDragDrop.SetDragHandler(ColorsGrid, new ColorDragSource(this));
    }

    /// <summary>提交当前单元格及行；验证失败时禁止移动，避免丢失尚未提交的编辑。</summary>
    private bool CommitEdits() => ColorsGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && ColorsGrid.CommitEdit(DataGridEditingUnit.Row, true);

    /// <summary>每次按实际命中位置读取行模型，不缓存会被虚拟化复用的行容器或可视索引。</summary>
    private ProductColorLineModel? DragLine(IDragInfo info)
    {
        if (!ReferenceEquals(info.VisualSource, ColorsGrid) || DataContext is not ProductColorsViewModel { CanReorder: true }) return null;
        var element = ColorsGrid.InputHitTest(info.DragStartPosition) as DependencyObject;
        while (element is not null && !ReferenceEquals(element, ColorsGrid))
        {
            if (element is FrameworkElement { Name: "ColorReorderHandle", IsEnabled: true, DataContext: ProductColorLineModel line }) return line;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    /// <summary>同一表格单行拖拽的插入槽换算为移除源行后的最终索引，拒绝外部、过期和原地拖放。</summary>
    private bool TryDestination(IDropInfo info, out ProductColorLineModel line, out int index)
    {
        line = null!;
        index = -1;
        if (!ReferenceEquals(info.DragInfo?.VisualSource, ColorsGrid) || !ReferenceEquals(info.VisualTarget, ColorsGrid)
            || DataContext is not ProductColorsViewModel vm || info.Data is not ProductColorLineModel color) return false;
        var sourceIndex = vm.Lines.Items.IndexOf(color);
        var slot = info.InsertIndex;
        if (sourceIndex < 0 || slot < 0 || slot > vm.Lines.Items.Count) return false;
        index = slot > sourceIndex ? slot - 1 : slot;
        line = color;
        return vm.CanMoveLineTo(line, index);
    }

    /// <summary>仅对有效目标显示移动效果和插入线，其他位置不接纳拖放。</summary>
    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        dropInfo.Effects = DragDropEffects.None;
        dropInfo.DropTargetAdorner = null;
        if (!TryDestination(dropInfo, out _, out _)) return;
        dropInfo.Effects = DragDropEffects.Move;
        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
    }

    /// <summary>落下时重新提交编辑并调用业务集合重排入口，不直接修改只读 ItemsSource。</summary>
    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        // DragOver 后权限、搜索或加载状态可能已经变化，落下时必须重新校验。
        if (!TryDestination(dropInfo, out var line, out var index) || !CommitEdits()) return;
        if (DataContext is ProductColorsViewModel vm && vm.MoveLineTo(line, index))
        {
            ColorsGrid.ScrollIntoView(line);
        }
    }

    /// <summary>提供拖拽的键盘等价入口，不占用 DataGrid 原生方向键和编辑快捷键。</summary>
    private void OnReorderKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt || DataContext is not ProductColorsViewModel vm) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var command = key == Key.Up ? vm.MoveLineUpCommand : key == Key.Down ? vm.MoveLineDownCommand : null;
        if (command is null) return;
        e.Handled = true;
        if (!command.CanExecute(vm.SelectedLine) || !CommitEdits()) return;
        command.Execute(vm.SelectedLine);
        if (vm.SelectedLine is not null) ColorsGrid.ScrollIntoView(vm.SelectedLine);
    }

    /// <summary>只拖动按下手柄所在的单行，多行选择和勾选不会扩大移动范围。</summary>
    private sealed class ColorDragSource(ProductColorsView owner) : DefaultDragHandler
    {
        /// <summary>按当前命中手柄及页面门禁决定是否允许开始拖动。</summary>
        public override bool CanStartDrag(IDragInfo dragInfo) => owner.DragLine(dragInfo) is not null;

        /// <summary>提交编辑后只发布手柄所在行作为移动载荷，不采用默认多选拖动逻辑。</summary>
        public override void StartDrag(IDragInfo dragInfo)
        {
            dragInfo.Effects = DragDropEffects.None;
            if (owner.DragLine(dragInfo) is not { } line || !owner.CommitEdits()
                || owner.DataContext is not ProductColorsViewModel vm || !vm.Lines.Items.Contains(line) || !vm.CanReorder) return;
            vm.SelectedLine = line;
            dragInfo.Data = line;
            dragInfo.Effects = DragDropEffects.Move;
        }
    }
}
