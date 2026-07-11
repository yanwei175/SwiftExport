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
    /// <summary>包含。</summary>
    Contains,
    /// <summary>不包含。</summary>
    DoesNotContain,
    /// <summary>以前缀开始。</summary>
    StartsWith,
    /// <summary>以后缀结束。</summary>
    EndsWith,
    /// <summary>等于。</summary>
    Equals,
    /// <summary>不等于。</summary>
    NotEquals,
    /// <summary>大于。</summary>
    GreaterThan,
    /// <summary>大于或等于。</summary>
    GreaterThanOrEqual,
    /// <summary>小于。</summary>
    LessThan,
    /// <summary>小于或等于。</summary>
    LessThanOrEqual,
    /// <summary>为 null。</summary>
    IsNull,
    /// <summary>不为 null。</summary>
    IsNotNull,
    /// <summary>文本为空。</summary>
    IsEmpty,
    /// <summary>文本不为空。</summary>
    IsNotEmpty
}

/// <summary>多个筛选条件的组合方式。</summary>
public enum GridFilterLogicalOperator
{
    /// <summary>全部条件成立。</summary>
    And,
    /// <summary>任一条件成立。</summary>
    Or
}

/// <summary>不可变筛选集合。</summary>
public sealed record GridFilterSet
{
    /// <summary>创建筛选描述快照。</summary>
    /// <param name="descriptors">筛选描述。</param>
    /// <param name="logicalOperator">组合方式。</param>
    public GridFilterSet(
        IEnumerable<GridFilterDescriptor>? descriptors,
        GridFilterLogicalOperator logicalOperator = GridFilterLogicalOperator.And)
    {
        Descriptors = descriptors?.ToArray() ?? Array.Empty<GridFilterDescriptor>();
        LogicalOperator = logicalOperator;
    }

    /// <summary>获取筛选描述快照。</summary>
    public IReadOnlyList<GridFilterDescriptor> Descriptors { get; }

    /// <summary>获取条件组合方式。</summary>
    public GridFilterLogicalOperator LogicalOperator { get; }

    /// <summary>获取空筛选集合。</summary>
    public static GridFilterSet Empty { get; } = new(Array.Empty<GridFilterDescriptor>());
}
