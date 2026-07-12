using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    /// <summary>
    /// 当前选区的只读业务快照。建议使用 Mode=OneWayToSource 绑定；框架仅在该属性存在绑定时构造快照。
    /// Cell/CellOrRowHeader 模式下原生 DataGrid.SelectedItem 不保证等于活动行，业务代码应使用
    /// CurrentRow 或快照的 CurrentRow。
    /// </summary>
    public static readonly DependencyProperty SelectionSnapshotProperty = DependencyProperty.RegisterAttached(
        "SelectionSnapshot",
        typeof(GridSelectionSnapshot),
        typeof(GridSelection),
        new FrameworkPropertyMetadata(null));

    /// <summary>
    /// 标识当前 DataGridCell 是否对应 DataGrid.CurrentCell。该状态仅用于宿主样式呈现，
    /// 通常由 <see cref="GridCurrentCellStateConverter"/> 通过 MultiBinding 计算。
    /// </summary>
    public static readonly DependencyProperty IsCurrentCellProperty = DependencyProperty.RegisterAttached(
        "IsCurrentCell",
        typeof(bool),
        typeof(GridSelection),
        new PropertyMetadata(false));

    /// <summary>
    /// 标识 DataGridRow 是否为当前活动 Cell 所在行。仅用于宿主样式，不表示完整多选行集合。
    /// </summary>
    public static readonly DependencyProperty IsCurrentRowProperty = DependencyProperty.RegisterAttached(
        "IsCurrentRow",
        typeof(bool),
        typeof(GridSelection),
        new PropertyMetadata(false));

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

    /// <summary>获取当前选区快照。</summary>
    public static GridSelectionSnapshot? GetSelectionSnapshot(DependencyObject element) =>
        (GridSelectionSnapshot?)element.GetValue(SelectionSnapshotProperty);

    /// <summary>设置选区快照绑定目标。业务页面通常仅用该方法承载 OneWayToSource Binding。</summary>
    public static void SetSelectionSnapshot(DependencyObject element, GridSelectionSnapshot? value) =>
        element.SetValue(SelectionSnapshotProperty, value);

    /// <summary>获取当前 Cell 样式状态。</summary>
    public static bool GetIsCurrentCell(DependencyObject element) =>
        (bool)element.GetValue(IsCurrentCellProperty);

    /// <summary>设置当前 Cell 样式状态，通常由宿主 MultiBinding 设置。</summary>
    public static void SetIsCurrentCell(DependencyObject element, bool value) =>
        element.SetValue(IsCurrentCellProperty, value);

    /// <summary>获取当前活动行样式状态。</summary>
    public static bool GetIsCurrentRow(DependencyObject element) =>
        (bool)element.GetValue(IsCurrentRowProperty);

    /// <summary>设置当前活动行样式状态，通常由宿主 MultiBinding 设置。</summary>
    public static void SetIsCurrentRow(DependencyObject element, bool value) =>
        element.SetValue(IsCurrentRowProperty, value);

    internal static void SetCurrentRowFromView(DataGrid dataGrid, object? value)
    {
        dataGrid.SetCurrentValue(CurrentRowProperty, value);
        UpdateBindingSource(dataGrid, CurrentRowProperty);
    }

    internal static void SetCurrentCellFromView(DataGrid dataGrid, GridCellReference? value)
    {
        dataGrid.SetCurrentValue(CurrentCellProperty, value);
        UpdateBindingSource(dataGrid, CurrentCellProperty);
    }

    internal static void SetCurrentColumnKeyFromView(DataGrid dataGrid, string? value)
    {
        dataGrid.SetCurrentValue(CurrentColumnKeyProperty, value);
        UpdateBindingSource(dataGrid, CurrentColumnKeyProperty);
    }

    internal static bool HasSelectionSnapshotBinding(DataGrid dataGrid) =>
        BindingOperations.IsDataBound(dataGrid, SelectionSnapshotProperty);

    internal static void SetSelectionSnapshotFromView(DataGrid dataGrid, GridSelectionSnapshot value)
    {
        dataGrid.SetCurrentValue(SelectionSnapshotProperty, value);
        UpdateBindingSource(dataGrid, SelectionSnapshotProperty);
    }

    internal static void SetCurrentColumnFromView(DataGrid dataGrid, DataGridColumn? value) =>
        dataGrid.SetValue(CurrentColumnPropertyKey, value);

    private static void UpdateBindingSource(DataGrid dataGrid, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(dataGrid, property)?.UpdateSource();
    }

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
