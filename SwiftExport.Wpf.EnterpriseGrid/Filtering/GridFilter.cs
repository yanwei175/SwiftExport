using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Filtering;

/// <summary>为原生 DataGrid 提供显式 Local/Remote 筛选模式。</summary>
public static class GridFilter
{
    /// <summary>筛选模式；Disabled 不修改现有 ICollectionView。</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode",
        typeof(GridFilterMode),
        typeof(GridFilter),
        new PropertyMetadata(GridFilterMode.Disabled, OnConfigurationChanged));

    /// <summary>不可变筛选集合，默认双向绑定。</summary>
    public static readonly DependencyProperty FilterSetProperty = DependencyProperty.RegisterAttached(
        "FilterSet",
        typeof(GridFilterSet),
        typeof(GridFilter),
        new FrameworkPropertyMetadata(
            GridFilterSet.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnConfigurationChanged));

    /// <summary>分组描述集合，默认双向绑定。</summary>
    public static readonly DependencyProperty GroupsProperty = DependencyProperty.RegisterAttached(
        "Groups",
        typeof(IReadOnlyList<GridGroupDescriptor>),
        typeof(GridFilter),
        new FrameworkPropertyMetadata(
            Array.Empty<GridGroupDescriptor>(),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnConfigurationChanged));

    /// <summary>Remote 模式下接收 <see cref="GridFilterRequest"/> 的宿主命令。</summary>
    public static readonly DependencyProperty RemoteFilterCommandProperty = DependencyProperty.RegisterAttached(
        "RemoteFilterCommand",
        typeof(ICommand),
        typeof(GridFilter),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>筛选应用前如何处理原生编辑事务。</summary>
    public static readonly DependencyProperty EditCommitModeProperty = DependencyProperty.RegisterAttached(
        "EditCommitMode",
        typeof(GridFilterEditCommitMode),
        typeof(GridFilter),
        new PropertyMetadata(GridFilterEditCommitMode.CommitCellAndRow));

    /// <summary>供 FilterRow 使用的默认列操作符。</summary>
    public static readonly DependencyProperty OperatorProperty = DependencyProperty.RegisterAttached(
        "Operator",
        typeof(GridFilterOperator),
        typeof(GridFilter),
        new PropertyMetadata(GridFilterOperator.Contains));

    private static readonly DependencyPropertyKey LastApplySucceededPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "LastApplySucceeded",
            typeof(bool),
            typeof(GridFilter),
            new PropertyMetadata(true));

    /// <summary>最近一次应用是否成功。</summary>
    public static readonly DependencyProperty LastApplySucceededProperty =
        LastApplySucceededPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey LastApplyErrorPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "LastApplyError",
            typeof(string),
            typeof(GridFilter),
            new PropertyMetadata(null));

    /// <summary>最近一次应用失败的文本原因。</summary>
    public static readonly DependencyProperty LastApplyErrorProperty = LastApplyErrorPropertyKey.DependencyProperty;

    public static GridFilterMode GetMode(DependencyObject element) => (GridFilterMode)element.GetValue(ModeProperty);

    public static void SetMode(DependencyObject element, GridFilterMode value) => element.SetValue(ModeProperty, value);

    public static GridFilterSet GetFilterSet(DependencyObject element) =>
        (GridFilterSet?)element.GetValue(FilterSetProperty) ?? GridFilterSet.Empty;

    public static void SetFilterSet(DependencyObject element, GridFilterSet? value) =>
        element.SetValue(FilterSetProperty, value ?? GridFilterSet.Empty);

    public static IReadOnlyList<GridGroupDescriptor> GetGroups(DependencyObject element) =>
        (IReadOnlyList<GridGroupDescriptor>?)element.GetValue(GroupsProperty) ?? Array.Empty<GridGroupDescriptor>();

    public static void SetGroups(DependencyObject element, IReadOnlyList<GridGroupDescriptor>? value) =>
        element.SetValue(GroupsProperty, value?.ToArray() ?? Array.Empty<GridGroupDescriptor>());

    public static ICommand? GetRemoteFilterCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(RemoteFilterCommandProperty);

    public static void SetRemoteFilterCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(RemoteFilterCommandProperty, value);

    public static GridFilterEditCommitMode GetEditCommitMode(DependencyObject element) =>
        (GridFilterEditCommitMode)element.GetValue(EditCommitModeProperty);

    public static void SetEditCommitMode(DependencyObject element, GridFilterEditCommitMode value) =>
        element.SetValue(EditCommitModeProperty, value);

    public static GridFilterOperator GetOperator(DependencyObject element) =>
        (GridFilterOperator)element.GetValue(OperatorProperty);

    public static void SetOperator(DependencyObject element, GridFilterOperator value) =>
        element.SetValue(OperatorProperty, value);

    public static bool GetLastApplySucceeded(DependencyObject element) =>
        (bool)element.GetValue(LastApplySucceededProperty);

    public static string? GetLastApplyError(DependencyObject element) =>
        (string?)element.GetValue(LastApplyErrorProperty);

    /// <summary>为指定原生列设置或替换一个筛选条件。</summary>
    public static void SetColumnFilter(
        DataGrid dataGrid,
        DataGridColumn column,
        object? value,
        GridFilterOperator? filterOperator = null)
    {
        var columnKey = GridColumn.GetKey(column);
        var fieldPath = GridColumn.GetFieldPath(column)
            ?? ((column as DataGridBoundColumn)?.Binding is System.Windows.Data.Binding binding
                ? binding.Path?.Path
                : null);
        if (string.IsNullOrWhiteSpace(columnKey) || string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new InvalidOperationException("ColumnFilter 需要非空且稳定的 GridColumn.Key 与 FieldPath/Binding.Path。");
        }

        var current = GetFilterSet(dataGrid);
        var descriptors = current.Descriptors
            .Where(descriptor => !string.Equals(descriptor.ColumnKey, columnKey, StringComparison.Ordinal))
            .ToList();
        if (value is not null && (value is not string text || !string.IsNullOrWhiteSpace(text)))
        {
            descriptors.Add(new GridFilterDescriptor(
                columnKey,
                fieldPath,
                filterOperator ?? GetOperator(column),
                value));
        }

        SetFilterSet(dataGrid, new GridFilterSet(descriptors, current.LogicalOperator));
    }

    /// <summary>清除全部列筛选，保留当前逻辑操作符。</summary>
    public static void Clear(DataGrid dataGrid)
    {
        SetFilterSet(dataGrid, new GridFilterSet(Array.Empty<GridFilterDescriptor>(), GetFilterSet(dataGrid).LogicalOperator));
    }

    internal static void SetApplyResult(DataGrid dataGrid, bool succeeded, string? error)
    {
        dataGrid.SetValue(LastApplySucceededPropertyKey, succeeded);
        dataGrid.SetValue(LastApplyErrorPropertyKey, error);
    }

    private static void OnConfigurationChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Filtering.ApplyConfiguration();
        }
    }
}

public enum GridFilterMode
{
    Disabled,
    Local,
    Remote
}

public enum GridFilterEditCommitMode
{
    CommitCell,
    CommitCellAndRow,
    CancelEdit
}
