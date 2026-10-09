/** 独立基线与业务当前值；原始单元格输入由展示会话另行短期保留。 */
export interface EditableRow<T extends object> {
  clientKey: string;
  uid: string;
  version: number;
  baseline: T;
  current: T;
}

/** 创建互不共享对象的编辑行，避免修改当前值污染服务器基线。 */
export function createEditableRow<T extends object>(
  clientKey: string,
  uid: string,
  version: number,
  value: T,
): EditableRow<T> {
  return {
    clientKey,
    uid,
    version,
    baseline: structuredClone(value),
    current: structuredClone(value),
  };
}

/** 用可写字段的相等比较判断修改，聚焦和 touched 不属于业务变化。 */
export function isRowDirty<T extends object>(row: EditableRow<T>): boolean {
  return Object.keys(row.baseline).some(
    (key) => row.baseline[key as keyof T] !== row.current[key as keyof T],
  );
}

/** 只替换指定行当前值，保留原始版本和基线。 */
export function updateEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
  patch: Partial<T>,
): EditableRow<T>[] {
  return rows.map((row) =>
    row.clientKey === clientKey
      ? { ...row, current: { ...row.current, ...patch } }
      : row,
  );
}

/** 放弃已确认可放弃的修改，恢复独立基线副本；调用者仍须检查保存锁。 */
export function discardEditableRows<T extends object>(
  rows: readonly EditableRow<T>[],
): EditableRow<T>[] {
  return rows.map((row) => ({
    ...row,
    current: structuredClone(row.baseline),
  }));
}

/** 只接纳已经完整核对的一行，不能借局部成功清除其他行草稿。 */
export function acceptEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
  uid: string,
  version: number,
  submitted: T,
): EditableRow<T>[] {
  return rows.map((row) =>
    row.clientKey === clientKey
      ? {
          ...row,
          uid,
          version,
          baseline: structuredClone(submitted),
          current: structuredClone(submitted),
        }
      : row,
  );
}
