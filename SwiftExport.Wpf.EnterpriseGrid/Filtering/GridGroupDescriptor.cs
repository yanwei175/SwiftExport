namespace SwiftExport.Wpf.EnterpriseGrid.Filtering;

/// <summary>基于稳定列与字段路径的分组描述。</summary>
/// <param name="ColumnKey">稳定列标识。</param>
/// <param name="FieldPath">分组字段路径。</param>
public sealed record GridGroupDescriptor(string ColumnKey, string FieldPath);

/// <summary>传递给远程筛选命令的完整请求，不包含服务或控件引用。</summary>
/// <param name="Filter">筛选快照。</param>
/// <param name="Groups">分组快照。</param>
public sealed record GridFilterRequest(
    GridFilterSet Filter,
    IReadOnlyList<GridGroupDescriptor> Groups);
