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

    /// <summary>获取筛选模式。</summary>
    public static GridFilterMode GetMode(DependencyObject element) => (GridFilterMode)element.GetValue(ModeProperty);

    /// <summary>设置筛选模式。</summary>
    public static void SetMode(DependencyObject element, GridFilterMode value) => element.SetValue(ModeProperty, value);

    /// <summary>获取不可变筛选集合。</summary>
    public static GridFilterSet GetFilterSet(DependencyObject element) =>
        (GridFilterSet?)element.GetValue(FilterSetProperty) ?? GridFilterSet.Empty;

    /// <summary>设置不可变筛选集合。</summary>
    public static void SetFilterSet(DependencyObject element, GridFilterSet? value) =>
        element.SetValue(FilterSetProperty, value ?? GridFilterSet.Empty);

    /// <summary>获取分组描述。</summary>
    public static IReadOnlyList<GridGroupDescriptor> GetGroups(DependencyObject element) =>
        (IReadOnlyList<GridGroupDescriptor>?)element.GetValue(GroupsProperty) ?? Array.Empty<GridGroupDescriptor>();

    /// <summary>设置分组描述并创建快照。</summary>
    public static void SetGroups(DependencyObject element, IReadOnlyList<GridGroupDescriptor>? value) =>
        element.SetValue(GroupsProperty, value?.ToArray() ?? Array.Empty<GridGroupDescriptor>());

    /// <summary>获取 Remote 模式宿主命令。</summary>
    public static ICommand? GetRemoteFilterCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(RemoteFilterCommandProperty);

    /// <summary>设置 Remote 模式宿主命令。</summary>
    public static void SetRemoteFilterCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(RemoteFilterCommandProperty, value);

    /// <summary>获取筛选前编辑事务策略。</summary>
    public static GridFilterEditCommitMode GetEditCommitMode(DependencyObject element) =>
        (GridFilterEditCommitMode)element.GetValue(EditCommitModeProperty);

    /// <summary>设置筛选前编辑事务策略。</summary>
    public static void SetEditCommitMode(DependencyObject element, GridFilterEditCommitMode value) =>
        element.SetValue(EditCommitModeProperty, value);

    /// <summary>获取列默认筛选操作符。</summary>
    public static GridFilterOperator GetOperator(DependencyObject element) =>
        (GridFilterOperator)element.GetValue(OperatorProperty);

    /// <summary>设置列默认筛选操作符。</summary>
    public static void SetOperator(DependencyObject element, GridFilterOperator value) =>
        element.SetValue(OperatorProperty, value);

    /// <summary>获取最近一次筛选应用结果。</summary>
    public static bool GetLastApplySucceeded(DependencyObject element) =>
        (bool)element.GetValue(LastApplySucceededProperty);

    /// <summary>获取最近一次筛选应用错误文本。</summary>
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

        dataGrid.SetCurrentValue(FilterSetProperty, new GridFilterSet(descriptors, current.LogicalOperator));
    }

    /// <summary>清除全部列筛选，保留当前逻辑操作符。</summary>
    public static void Clear(DataGrid dataGrid)
    {
        dataGrid.SetCurrentValue(
            FilterSetProperty,
            new GridFilterSet(Array.Empty<GridFilterDescriptor>(), GetFilterSet(dataGrid).LogicalOperator));
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

/// <summary>筛选执行模式。</summary>
public enum GridFilterMode
{
    /// <summary>关闭框架筛选并恢复原 View Filter。</summary>
    Disabled,
    /// <summary>在现有 ICollectionView 上组合本地谓词。</summary>
    Local,
    /// <summary>只向宿主命令发送描述，不修改本地视图。</summary>
    Remote
}

/// <summary>筛选应用前的编辑事务策略。</summary>
public enum GridFilterEditCommitMode
{
    /// <summary>仅提交当前 Cell。</summary>
    CommitCell,
    /// <summary>提交当前 Cell 和 Row。</summary>
    CommitCellAndRow,
    /// <summary>取消当前 Cell 和 Row 编辑。</summary>
    CancelEdit
}
