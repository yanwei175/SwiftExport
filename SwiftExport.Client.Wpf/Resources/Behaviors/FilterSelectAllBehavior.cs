using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>同步筛选候选列表与全选复选框的三态，不生成或解释筛选条件。</summary>
public sealed class FilterSelectAllBehavior : Behavior<ListBox>
{
    private bool _synchronizing;

    /// <summary>全选为 true、全不选为 false、部分选择为 null。</summary>
    public bool? IsAllSelected
    {
        get => (bool?)GetValue(IsAllSelectedProperty);
        set => SetValue(IsAllSelectedProperty, value);
    }

    /// <summary>供模板内全选复选框双向绑定的三态属性。</summary>
    public static readonly DependencyProperty IsAllSelectedProperty = DependencyProperty.Register(
        nameof(IsAllSelected), typeof(bool?), typeof(FilterSelectAllBehavior),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((FilterSelectAllBehavior)sender).ApplySelection()));

    /// <summary>监听列表当前选择及候选变化，不保留行对象或业务模型。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        // 库卸载时仍会清理旧列表选区；局部值保证模板拆除后仍为多选，避免回落为 Single。
        AssociatedObject.SelectionMode = SelectionMode.Multiple;
        AssociatedObject.SelectionChanged += OnSelectionChanged;
        AssociatedObject.Loaded += OnLoaded;
        ((INotifyCollectionChanged)AssociatedObject.Items).CollectionChanged += OnItemsChanged;
        UpdateState();
    }

    /// <summary>完整解除列表及集合事件，支持模板移除或替换。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.SelectionChanged -= OnSelectionChanged;
        AssociatedObject.Loaded -= OnLoaded;
        ((INotifyCollectionChanged)AssociatedObject.Items).CollectionChanged -= OnItemsChanged;
        base.OnDetaching();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();
    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateState();
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateState();

    /// <summary>只接受明确的全选/取消操作；部分选择状态仅用于展示。</summary>
    private void ApplySelection()
    {
        if (_synchronizing || AssociatedObject is not { IsLoaded: true } list || IsAllSelected is not bool value) return;
        if (value) list.SelectAll();
        else list.UnselectAll();
        UpdateState();
    }

    private void UpdateState()
    {
        if (_synchronizing || AssociatedObject is not { } list) return;
        _synchronizing = true;
        try
        {
            bool? state = list.SelectedItems.Count == 0 ? false
                : list.SelectedItems.Count == list.Items.Count ? true : null;
            SetCurrentValue(IsAllSelectedProperty, state);
        }
        finally { _synchronizing = false; }
    }
}
