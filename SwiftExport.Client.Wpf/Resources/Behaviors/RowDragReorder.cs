using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GongSolutions.Wpf.DragDrop;
using SwiftExportClient.Mvvm;
using GongDragDrop = GongSolutions.Wpf.DragDrop.DragDrop;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>桥接表格单行手柄拖动与业务命令；不修改集合、编号或保存状态，独占宿主的 Gong 拖放接线。</summary>
public static class RowDragReorder
{
    /// <summary>控制拖拽接线及业务编辑门禁，禁用时解除当前行为拥有的事件和 Gong 配置。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(RowDragReorder), new PropertyMetadata(false, OnEnabledChanged));
    /// <summary>接收 RowReorderRequest；CanExecute 与 Execute 均由业务页面校验。</summary>
    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(RowDragReorder));
    /// <summary>标记允许开始拖动的行首元素，其他单元格保持原生编辑和选择语义。</summary>
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(RowDragReorder), new PropertyMetadata(false));
    /// <summary>仅允许宿主明确支持的业务分组，不解除筛选或排序门禁。</summary>
    public static readonly DependencyProperty AllowBusinessGroupingProperty = DependencyProperty.RegisterAttached(
        "AllowBusinessGrouping", typeof(bool), typeof(RowDragReorder), new PropertyMetadata(false));
    /// <summary>可选视图编辑提交回调；箱单用它同时提交组头编辑器，缺省提交当前单元格和行。</summary>
    public static readonly DependencyProperty CommitEditsProperty = DependencyProperty.RegisterAttached(
        "CommitEdits", typeof(Func<bool>), typeof(RowDragReorder));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(RowDragReorder));

    /// <summary>读取当前启用状态。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    /// <summary>设置当前启用状态。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    /// <summary>读取业务重排命令。</summary>
    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);
    /// <summary>设置业务重排命令。</summary>
    public static void SetMoveCommand(DependencyObject element, ICommand? value) => element.SetValue(MoveCommandProperty, value);
    /// <summary>读取手柄标记。</summary>
    public static bool GetIsHandle(DependencyObject element) => (bool)element.GetValue(IsHandleProperty);
    /// <summary>设置手柄标记。</summary>
    public static void SetIsHandle(DependencyObject element, bool value) => element.SetValue(IsHandleProperty, value);
    /// <summary>读取业务分组支持开关。</summary>
    public static bool GetAllowBusinessGrouping(DependencyObject element) => (bool)element.GetValue(AllowBusinessGroupingProperty);
    /// <summary>设置业务分组支持开关。</summary>
    public static void SetAllowBusinessGrouping(DependencyObject element, bool value) => element.SetValue(AllowBusinessGroupingProperty, value);
    /// <summary>读取宿主编辑提交回调。</summary>
    public static Func<bool>? GetCommitEdits(DependencyObject element) => (Func<bool>?)element.GetValue(CommitEditsProperty);
    /// <summary>设置宿主编辑提交回调。</summary>
    public static void SetCommitEdits(DependencyObject element, Func<bool>? value) => element.SetValue(CommitEditsProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid) throw new InvalidOperationException("行拖拽重排只能用于原生 DataGrid。");
        if (grid.GetValue(StateProperty) is State previous) previous.Dispose();
        grid.ClearValue(StateProperty);
        if (e.NewValue is true)
        {
            var state = new State(grid);
            grid.SetValue(StateProperty, state);
            state.Connect();
        }
    }

    /// <summary>每个表格拥有一个可释放的桥接器，不保留行容器和页面 ViewModel。</summary>
    private sealed class State(DataGrid grid) : DefaultDragHandler, IDropTarget, IDisposable
    {
        private bool _attached;

        /// <summary>订阅宿主生命周期，已加载时立即安装桥接。</summary>
        public void Connect()
        {
            grid.Loaded += OnLoaded;
            grid.Unloaded += OnUnloaded;
            if (grid.IsLoaded) Attach();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        /// <summary>Loaded 后安装拖放及键盘桥接；键盘处理只作用于非编辑状态的 Alt 方向键。</summary>
        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            GongDragDrop.SetDragHandler(grid, this);
            GongDragDrop.SetDropHandler(grid, this);
            GongDragDrop.SetIsDragSource(grid, true);
            GongDragDrop.SetIsDropTarget(grid, true);
            grid.PreviewKeyDown += OnKeyDown;
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            grid.PreviewKeyDown -= OnKeyDown;
            if (ReferenceEquals(GongDragDrop.GetDragHandler(grid), this))
            {
                GongDragDrop.SetIsDragSource(grid, false);
                grid.ClearValue(GongDragDrop.DragHandlerProperty);
            }
            if (ReferenceEquals(GongDragDrop.GetDropHandler(grid), this))
            {
                GongDragDrop.SetIsDropTarget(grid, false);
                grid.ClearValue(GongDragDrop.DropHandlerProperty);
            }
        }

        private bool CanReorder() => _attached && GetIsEnabled(grid) && grid.IsEnabled && !grid.IsReadOnly && !grid.CanUserSortColumns
            && RowReorderRequest.IsViewInSourceOrder(grid.Items, GetAllowBusinessGrouping(grid));

        private bool Commit() => GetCommitEdits(grid)?.Invoke()
            ?? (grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true));

        /// <summary>按当前手柄的数据对象构造载荷，滚动回收后不沿用曾经命中的行。</summary>
        private object? FindSource(IDragInfo info)
        {
            if (!CanReorder() || !ReferenceEquals(info.VisualSource, grid)) return null;
            var hit = grid.InputHitTest(info.DragStartPosition) as DependencyObject;
            while (hit is not null && !ReferenceEquals(hit, grid))
            {
                if (GetIsHandle(hit) && hit is FrameworkElement { IsEnabled: true, DataContext: { } item } && grid.Items.Contains(item)) return item;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        /// <summary>只允许在当前可编辑手柄开始拖动。</summary>
        public override bool CanStartDrag(IDragInfo dragInfo) => FindSource(dragInfo) is not null;

        /// <summary>编辑提交后仅发布单行对象；多选及勾选不会扩大拖动范围。</summary>
        public override void StartDrag(IDragInfo dragInfo)
        {
            dragInfo.Data = null;
            dragInfo.Effects = DragDropEffects.None;
            if (FindSource(dragInfo) is not { } item || !Commit() || !CanReorder() || !grid.Items.Contains(item)) return;
            grid.SelectedItem = item;
            dragInfo.Data = item;
            dragInfo.Effects = DragDropEffects.Move;
        }

        /// <summary>目标锚点优先于可视插入索引，分组表只能落在真实行上，不能落在组头或组间空白。</summary>
        private RowReorderRequest? Request(IDropInfo info)
        {
            if (!CanReorder() || !ReferenceEquals(info.DragInfo?.VisualSource, grid) || !ReferenceEquals(info.VisualTarget, grid)
                || info.Data is not { } source || !grid.Items.Contains(source)) return null;
            if (grid.Items.GroupDescriptions.Count > 0 && info.VisualTargetItem is not DataGridRow) return null;
            if (grid.Items.GroupDescriptions.Count > 0 && info.VisualTargetItem is not DataGridRow) return null;
            if (info.TargetItem is { } target && grid.Items.Contains(target))
                return new(source, target, info.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem) ? RowDropPosition.After : RowDropPosition.Before);
            if (grid.Items.GroupDescriptions.Count > 0 || info.TargetItem is not null || info.InsertIndex != grid.Items.Count) return null;
            return new(source, null, RowDropPosition.End);
        }

        /// <summary>只显示业务命令认可的插入位置。</summary>
        void IDropTarget.DragOver(IDropInfo info)
        {
            info.Effects = DragDropEffects.None;
            info.DropTargetAdorner = null;
            if (Request(info) is not { } request || GetMoveCommand(grid)?.CanExecute(request) != true) return;
            info.Effects = DragDropEffects.Move;
            info.DropTargetAdorner = DropTargetAdorners.Insert;
        }

        /// <summary>落下时重新提交编辑、解析当前目标并执行命令，防止拖动过程中门禁变化。</summary>
        void IDropTarget.Drop(IDropInfo info)
        {
            if (Request(info) is not { } request || GetMoveCommand(grid)?.CanExecute(request) != true || !Commit()) return;
            Execute(request);
        }

        private void Execute(RowReorderRequest request)
        {
            if (!CanReorder() || !grid.Items.Contains(request.SourceItem) || GetMoveCommand(grid) is not { } command || !command.CanExecute(request)) return;
            command.Execute(request);
            if (grid.Items.Contains(request.SourceItem)) grid.ScrollIntoView(request.SourceItem);
        }

        /// <summary>Alt 方向键与拖拽使用同一业务入口；文本框、下拉框和正在编辑的单元格保留自身按键。</summary>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.KeyboardDevice.Modifiers != ModifierKeys.Alt || !CanReorder()) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is not (Key.Up or Key.Down)) return;
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused is not null && !ReferenceEquals(focused, grid))
            {
                if (focused is TextBoxBase or ComboBox || focused is DataGridCell { IsEditing: true }) return;
                focused = VisualTreeHelper.GetParent(focused);
            }
            var source = grid.CurrentItem ?? grid.SelectedItem;
            var index = source is null ? -1 : grid.Items.IndexOf(source);
            var next = index + (key == Key.Up ? -1 : 1);
            if (index < 0 || next < 0 || next >= grid.Items.Count) return;
            var request = new RowReorderRequest(source!, grid.Items[next], key == Key.Up ? RowDropPosition.Before : RowDropPosition.After);
            if (GetMoveCommand(grid)?.CanExecute(request) != true || !Commit()) return;
            e.Handled = true;
            Execute(request);
        }

        /// <summary>禁用时同时解除 Loaded、Unloaded 和已安装的输入桥接。</summary>
        public void Dispose()
        {
            grid.Loaded -= OnLoaded;
            grid.Unloaded -= OnUnloaded;
            Detach();
        }
    }
}
