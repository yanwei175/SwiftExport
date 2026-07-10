using System.Windows;
using System.Windows.Controls;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Selection;

/// <summary>
/// 将原生 DataGrid 的选择和当前单元格状态桥接为可绑定附加属性。
/// </summary>
public static class GridSelection
{
    /// <summary>
    /// 控制单击 Cell 内任意子控件时是否优先同步当前行和当前单元格。
    /// </summary>
    public static readonly DependencyProperty IsCellClickSelectionEnabledProperty = DependencyProperty.RegisterAttached(
        "IsCellClickSelectionEnabled",
        typeof(bool),
        typeof(GridSelection),
        new PropertyMetadata(false));

    /// <summary>
    /// 当前行数据项。默认双向绑定。
    /// </summary>
    public static readonly DependencyProperty CurrentRowProperty = DependencyProperty.RegisterAttached(
        "CurrentRow",
        typeof(object),
        typeof(GridSelection),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurrentRowChanged));

    /// <summary>
    /// 当前单元格的数据引用。默认双向绑定。
    /// </summary>
    public static readonly DependencyProperty CurrentCellProperty = DependencyProperty.RegisterAttached(
        "CurrentCell",
        typeof(GridCellReference),
        typeof(GridSelection),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurrentCellChanged));

    /// <summary>
    /// 当前列的稳定 ColumnKey。默认双向绑定。
    /// </summary>
    public static readonly DependencyProperty CurrentColumnKeyProperty = DependencyProperty.RegisterAttached(
        "CurrentColumnKey",
        typeof(string),
        typeof(GridSelection),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurrentColumnKeyChanged));

    private static readonly DependencyPropertyKey CurrentColumnPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "CurrentColumn",
        typeof(DataGridColumn),
        typeof(GridSelection),
        new PropertyMetadata(null));

    /// <summary>
    /// 当前原生 DataGridColumn，仅用于 View 层诊断和命令参数。
    /// </summary>
    public static readonly DependencyProperty CurrentColumnProperty = CurrentColumnPropertyKey.DependencyProperty;

    /// <summary>获取是否启用 Cell 子控件点击选择同步。</summary>
    public static bool GetIsCellClickSelectionEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsCellClickSelectionEnabledProperty);

    /// <summary>设置是否启用 Cell 子控件点击选择同步。</summary>
    public static void SetIsCellClickSelectionEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsCellClickSelectionEnabledProperty, value);

    /// <summary>获取当前行数据项。</summary>
    public static object? GetCurrentRow(DependencyObject element) => element.GetValue(CurrentRowProperty);

    /// <summary>设置当前行数据项。</summary>
    public static void SetCurrentRow(DependencyObject element, object? value) =>
        element.SetValue(CurrentRowProperty, value);

    /// <summary>获取当前单元格数据引用。</summary>
    public static GridCellReference? GetCurrentCell(DependencyObject element) =>
        (GridCellReference?)element.GetValue(CurrentCellProperty);

    /// <summary>设置当前单元格数据引用。</summary>
    public static void SetCurrentCell(DependencyObject element, GridCellReference? value) =>
        element.SetValue(CurrentCellProperty, value);

    /// <summary>获取当前列的稳定 ColumnKey。</summary>
    public static string? GetCurrentColumnKey(DependencyObject element) =>
        (string?)element.GetValue(CurrentColumnKeyProperty);

    /// <summary>设置当前列的稳定 ColumnKey。</summary>
    public static void SetCurrentColumnKey(DependencyObject element, string? value) =>
        element.SetValue(CurrentColumnKeyProperty, value);

    /// <summary>获取当前原生 DataGridColumn。</summary>
    public static DataGridColumn? GetCurrentColumn(DependencyObject element) =>
        (DataGridColumn?)element.GetValue(CurrentColumnProperty);

    internal static void SetCurrentRowFromView(DataGrid dataGrid, object? value) =>
        dataGrid.SetCurrentValue(CurrentRowProperty, value);

    internal static void SetCurrentCellFromView(DataGrid dataGrid, GridCellReference? value) =>
        dataGrid.SetCurrentValue(CurrentCellProperty, value);

    internal static void SetCurrentColumnKeyFromView(DataGrid dataGrid, string? value) =>
        dataGrid.SetCurrentValue(CurrentColumnKeyProperty, value);

    internal static void SetCurrentColumnFromView(DataGrid dataGrid, DataGridColumn? value) =>
        dataGrid.SetValue(CurrentColumnPropertyKey, value);

    private static void OnCurrentRowChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Selection.ApplyCurrentRow(e.NewValue);
        }
    }

    private static void OnCurrentCellChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Selection.ApplyCurrentCell((GridCellReference?)e.NewValue);
        }
    }

    private static void OnCurrentColumnKeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Selection.ApplyCurrentColumnKey((string?)e.NewValue);
        }
    }
}
