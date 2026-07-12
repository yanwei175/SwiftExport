namespace SwiftExport.Wpf.EnterpriseGrid.Selection;

/// <summary>
/// 描述 DataGrid 当前活动位置和完整选区的只读快照，不持有任何虚拟化容器。
/// </summary>
/// <param name="CurrentRow">
/// 活动单元格所在行；在 FullRow 模式下等同于原生 DataGrid.SelectedItem。
/// </param>
/// <param name="CurrentCell">活动单元格的数据引用。</param>
/// <param name="SelectedRows">
/// 选区涉及的行，按当前视图从上到下去重排列。第一项是选区最上方行，未必是 <paramref name="CurrentRow"/>。
/// </param>
/// <param name="SelectedCellCount">原生 DataGrid.SelectedCells 的数量。</param>
public sealed record GridSelectionSnapshot(
    object? CurrentRow,
    GridCellReference? CurrentCell,
    IReadOnlyList<object> SelectedRows,
    int SelectedCellCount);
