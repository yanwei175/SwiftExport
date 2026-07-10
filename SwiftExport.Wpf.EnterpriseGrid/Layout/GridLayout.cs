using System.Windows;
using System.Windows.Controls;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout;

/// <summary>将列布局状态桥接到 MVVM 的附加属性。</summary>
public static class GridLayout
{
    /// <summary>是否启用布局状态双向同步。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridLayout),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>当前布局状态，默认双向绑定。</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(GridLayoutState),
        typeof(GridLayout),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnStateChanged));

    private static readonly DependencyPropertyKey LastApplyResultPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "LastApplyResult",
            typeof(GridLayoutApplyResult),
            typeof(GridLayout),
            new PropertyMetadata(null));

    /// <summary>最近一次外部 State 恢复结果。</summary>
    public static readonly DependencyProperty LastApplyResultProperty = LastApplyResultPropertyKey.DependencyProperty;

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static GridLayoutState? GetState(DependencyObject element) =>
        (GridLayoutState?)element.GetValue(StateProperty);

    public static void SetState(DependencyObject element, GridLayoutState? value) =>
        element.SetValue(StateProperty, value);

    public static GridLayoutApplyResult? GetLastApplyResult(DependencyObject element) =>
        (GridLayoutApplyResult?)element.GetValue(LastApplyResultProperty);

    /// <summary>立即捕获布局并写回 State Binding。</summary>
    public static GridLayoutState Capture(DataGrid dataGrid)
    {
        var state = GridLayoutManager.Capture(dataGrid);
        EnterpriseGridCore.GetCoordinator(dataGrid)?.Layout.SetStateFromGrid(state);
        return state;
    }

    internal static void SetStateFromGrid(DataGrid dataGrid, GridLayoutState state) =>
        dataGrid.SetCurrentValue(StateProperty, state);

    internal static void SetApplyResult(DataGrid dataGrid, GridLayoutApplyResult result) =>
        dataGrid.SetValue(LastApplyResultPropertyKey, result);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Layout.RefreshEnabledState();
        }
    }

    private static void OnStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Layout.ApplyState((GridLayoutState?)e.NewValue);
        }
    }
}
