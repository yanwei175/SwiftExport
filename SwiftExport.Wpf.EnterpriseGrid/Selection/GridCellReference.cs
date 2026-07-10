namespace SwiftExport.Wpf.EnterpriseGrid.Selection;

/// <summary>
/// 使用数据项和稳定列标识描述当前单元格，不持有虚拟化的 DataGridCell 容器。
/// </summary>
/// <param name="Item">当前单元格所属的数据项。</param>
/// <param name="ColumnKey">当前列的稳定标识；未配置稳定标识时为 null。</param>
public sealed record GridCellReference(object? Item, string? ColumnKey);
