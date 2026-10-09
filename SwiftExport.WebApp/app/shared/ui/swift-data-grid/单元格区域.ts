import type { SwiftDataGridFillDownIntent, SwiftDataGridRowKey } from "./类型";

/** 区域只持有稳定行列身份，不保存字段值、草稿或原始输入。 */
export interface GridCell {
  rowKey: SwiftDataGridRowKey;
  columnKey: string;
}

/** 锚点和终点限定矩形，布局签名防止分页、排序或隐藏列后错误继承选择。 */
export interface GridCellSelection {
  anchor: GridCell;
  end: GridCell;
  layout: string;
}

/** 归一化范围用于判断视觉选择和发送填充意图。 */
export interface GridCellRange {
  firstRow: number;
  lastRow: number;
  firstColumn: number;
  lastColumn: number;
}

/** 采用有类型的 JSON 行身份，避免字符串和数字行键碰撞；页码同样隔离。 */
export function gridLayoutSignature(
  rowKeys: readonly SwiftDataGridRowKey[],
  columnKeys: readonly string[],
  pageIndex = 1,
  pageSize = rowKeys.length,
): string {
  return JSON.stringify([rowKeys, columnKeys, pageIndex, pageSize]);
}

/** 仅当前布局中的完整锚点/终点有效，旧布局不能重新映射到不同业务行。 */
export function resolveGridCellRange(
  selection: GridCellSelection | null,
  rowKeys: readonly SwiftDataGridRowKey[],
  columnKeys: readonly string[],
  layout: string,
): GridCellRange | null {
  if (!selection || selection.layout !== layout) return null;
  const anchorRow = rowKeys.indexOf(selection.anchor.rowKey);
  const endRow = rowKeys.indexOf(selection.end.rowKey);
  const anchorColumn = columnKeys.indexOf(selection.anchor.columnKey);
  const endColumn = columnKeys.indexOf(selection.end.columnKey);
  if ([anchorRow, endRow, anchorColumn, endColumn].some((value) => value < 0))
    return null;
  return {
    firstRow: Math.min(anchorRow, endRow),
    lastRow: Math.max(anchorRow, endRow),
    firstColumn: Math.min(anchorColumn, endColumn),
    lastColumn: Math.max(anchorColumn, endColumn),
  };
}

/** 键盘移动只移动区域终点，边缘钳制；不修改行数据或编辑器焦点内文本。 */
export function moveGridCell(
  cell: GridCell,
  rowKeys: readonly SwiftDataGridRowKey[],
  columnKeys: readonly string[],
  rowOffset: number,
  columnOffset: number,
): GridCell | null {
  const row = rowKeys.indexOf(cell.rowKey);
  const column = columnKeys.indexOf(cell.columnKey);
  if (row < 0 || column < 0) return null;
  const rowKey =
    rowKeys[Math.max(0, Math.min(rowKeys.length - 1, row + rowOffset))];
  const columnKey =
    columnKeys[
      Math.max(0, Math.min(columnKeys.length - 1, column + columnOffset))
    ];
  return rowKey === undefined || columnKey === undefined
    ? null
    : { rowKey, columnKey };
}

/** 列宽只接受有限像素；与偏好白名单一致限制到 48～1000，不改变数据内容。 */
export function constrainGridColumnWidth(width: number): number {
  return Number.isFinite(width)
    ? Math.round(Math.min(1000, Math.max(48, width)))
    : 160;
}

/**
 * 只从当前可见矩形生成逐列意图，不读取字段值或写入行对象。
 * 关联字段无需有表格列；重复标识去重、空数组禁用该列，非法空白标识拒绝整个区域。
 */
export function buildGridFillDownIntent<T extends object>(
  range: GridCellRange | null,
  rows: readonly T[],
  columnKeys: readonly string[],
  canFillColumn: (columnKey: string) => boolean,
  isWritable: (row: T, columnKey: string) => boolean,
  getFields: (columnKey: string) => readonly string[] = (columnKey) => [
    columnKey,
  ],
): SwiftDataGridFillDownIntent<T> | null {
  if (!range || range.firstRow >= range.lastRow) return null;
  const source = rows[range.firstRow];
  if (!source) return null;
  const columns: SwiftDataGridFillDownIntent<T>["columns"][number][] = [];
  for (const columnKey of columnKeys.slice(
    range.firstColumn,
    range.lastColumn + 1,
  )) {
    if (!canFillColumn(columnKey) || !isWritable(source, columnKey)) continue;
    const declaredFields = getFields(columnKey);
    if (!declaredFields.length) continue;
    // 不默默修正配置或发出部分区域；字段实际含义和可写白名单仍由 feature 核对。
    if (declaredFields.some((field) => !field || /\s/.test(field))) return null;
    const fields = [...new Set(declaredFields)];
    const targets = rows
      .slice(range.firstRow + 1, range.lastRow + 1)
      .filter((row) => isWritable(row, columnKey));
    if (targets.length) columns.push({ columnKey, fields, source, targets });
  }
  return columns.length ? { columns } : null;
}
