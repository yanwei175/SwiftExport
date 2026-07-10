namespace SwiftExport.Wpf.EnterpriseGrid.Filtering;

/// <summary>只包含稳定列与字段语义的筛选描述，不持有 DataGridColumn 或编辑器。</summary>
/// <param name="ColumnKey">稳定列标识。</param>
/// <param name="FieldPath">数据字段路径。</param>
/// <param name="Operator">筛选操作符。</param>
/// <param name="Value">筛选值；空值操作符可忽略。</param>
/// <param name="IsCaseSensitive">字符串比较是否区分大小写。</param>
public sealed record GridFilterDescriptor(
    string ColumnKey,
    string FieldPath,
    GridFilterOperator Operator,
    object? Value = null,
    bool IsCaseSensitive = false);

/// <summary>筛选操作符。</summary>
public enum GridFilterOperator
{
    Contains,
    DoesNotContain,
    StartsWith,
    EndsWith,
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    IsNull,
    IsNotNull,
    IsEmpty,
    IsNotEmpty
}

/// <summary>多个筛选条件的组合方式。</summary>
public enum GridFilterLogicalOperator
{
    And,
    Or
}

/// <summary>不可变筛选集合。</summary>
public sealed record GridFilterSet
{
    public GridFilterSet(
        IEnumerable<GridFilterDescriptor>? descriptors,
        GridFilterLogicalOperator logicalOperator = GridFilterLogicalOperator.And)
    {
        Descriptors = descriptors?.ToArray() ?? Array.Empty<GridFilterDescriptor>();
        LogicalOperator = logicalOperator;
    }

    public IReadOnlyList<GridFilterDescriptor> Descriptors { get; }

    public GridFilterLogicalOperator LogicalOperator { get; }

    public static GridFilterSet Empty { get; } = new(Array.Empty<GridFilterDescriptor>());
}
