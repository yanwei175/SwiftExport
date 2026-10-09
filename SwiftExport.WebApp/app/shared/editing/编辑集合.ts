/** feature 对可写字段执行的明确相等策略；归一化与特殊值结构由业务提供。 */
export type EditableValueEquality<T extends object> = (
  baseline: T,
  current: T,
) => boolean;

/**
 * 独立基线与唯一业务当前值；T 应是可写字段投影，Uid/Version 单独作为不可编辑元数据。
 * origin/deleted 只表达本地行生命周期，原始单元格输入仍由展示会话短期保留。
 */
export interface EditableRow<T extends object> {
  readonly clientKey: string;
  readonly uid: string;
  readonly version: number;
  readonly origin?: "existing" | "created";
  readonly deleted?: boolean;
  readonly valuesEqual?: EditableValueEquality<T>;
  baseline: T;
  current: T;
}

/** 由同一平面集合派生的互斥变更视图，不另存业务当前值，也不直接生成后端 DTO。 */
export interface EditableChanges<T extends object> {
  readonly created: readonly EditableRow<T>[];
  readonly updated: readonly EditableRow<T>[];
  readonly deleted: readonly EditableRow<T>[];
}

/**
 * 创建默认既有行及互不共享的字段副本，避免修改当前值污染服务器基线。
 * 默认比较可写投影中的普通对象、数组、日期与原始值；其他结构或归一化须提供 valuesEqual。
 */
export function createEditableRow<T extends object>(
  clientKey: string,
  uid: string,
  version: number,
  value: T,
  valuesEqual?: EditableValueEquality<T>,
): EditableRow<T> {
  return {
    clientKey,
    uid,
    version,
    origin: "existing",
    deleted: false,
    valuesEqual,
    baseline: structuredClone(value),
    current: structuredClone(value),
  };
}

/** 创建立即属于新增变更的本地行；空 Uid/零 Version 不是服务器身份或并发凭据。 */
export function createCreatedEditableRow<T extends object>(
  clientKey: string,
  value: T,
  valuesEqual?: EditableValueEquality<T>,
): EditableRow<T> {
  return {
    ...createEditableRow(clientKey, "", 0, value, valuesEqual),
    origin: "created",
  };
}

/** 用行生命周期及可写字段比较判断业务修改，聚焦、touched 和单元格缓冲不属于此判断。 */
export function isRowDirty<T extends object>(row: EditableRow<T>): boolean {
  return (
    row.origin === "created" ||
    row.deleted === true ||
    !(row.valuesEqual ?? areWritableValuesEqual)(row.baseline, row.current)
  );
}

/** 追加行并检查稳定 ClientKey 唯一性；不能用重复键覆盖已有草稿或派生多个操作。 */
export function addEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  row: EditableRow<T>,
): EditableRow<T>[] {
  const nextRows = [...rows, row];
  assertUniqueClientKeys(nextRows);
  return nextRows;
}

/** 只替换未删除行的可写当前值，保留身份、原始版本和基线；删除行须先恢复再编辑。 */
export function updateEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
  patch: Partial<T>,
): EditableRow<T>[] {
  return rows.map((row) =>
    row.clientKey === clientKey && row.deleted !== true
      ? { ...row, current: { ...row.current, ...patch } }
      : row,
  );
}

/** 删除新增行时直接移除；既有行仅标记删除并保留删除前草稿和基线供恢复。 */
export function removeEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
): EditableRow<T>[] {
  return rows
    .filter((row) => row.clientKey !== clientKey || row.origin !== "created")
    .map((row) =>
      row.clientKey === clientKey ? { ...row, deleted: true } : row,
    );
}

/** 恢复既有删除行并保留删除前草稿，不用基线覆盖用户已编辑的字段。 */
export function restoreEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
): EditableRow<T>[] {
  return rows.map((row) =>
    row.clientKey === clientKey && row.deleted === true
      ? { ...row, deleted: false }
      : row,
  );
}

/** 返回未删除行的展示视图；隐藏删除行不移除其恢复信息，也不改变其他行。 */
export function getVisibleEditableRows<T extends object>(
  rows: readonly EditableRow<T>[],
): EditableRow<T>[] {
  return rows.filter((row) => row.deleted !== true);
}

/** 按稳定 ClientKey 派生互斥增改删；既有行先修改再删除只形成删除操作。 */
export function getEditableChanges<T extends object>(
  rows: readonly EditableRow<T>[],
): EditableChanges<T> {
  assertUniqueClientKeys(rows);
  const created: EditableRow<T>[] = [];
  const updated: EditableRow<T>[] = [];
  const deleted: EditableRow<T>[] = [];
  for (const row of rows) {
    if (row.deleted === true) {
      if (row.origin !== "created") deleted.push(row);
    } else if (row.origin === "created") {
      created.push(row);
    } else if (isRowDirty(row)) {
      updated.push(row);
    }
  }
  return { created, updated, deleted };
}

/** 放弃新增行并恢复既有行的独立基线副本和可见状态；调用者仍须先检查完整保存锁。 */
export function discardEditableRows<T extends object>(
  rows: readonly EditableRow<T>[],
): EditableRow<T>[] {
  return rows
    .filter((row) => row.origin !== "created")
    .map((row) => ({
      ...row,
      deleted: false,
      current: structuredClone(row.baseline),
    }));
}

/**
 * 只接纳 feature 已完整核对的新增/更新回执及冻结字段，采用真实 Uid/Version 并转为既有行。
 * 不解释 HTTP/业务结果，不自动提高版本，不借局部成功清除其他草稿；删除须使用删除接纳入口。
 */
export function acceptEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
  uid: string,
  version: number,
  submitted: T,
): EditableRow<T>[] {
  return rows.map((row) => {
    if (row.clientKey !== clientKey) return row;
    if (row.deleted === true) {
      throw new Error("删除行必须通过已确认删除回执接纳。");
    }
    return {
      ...row,
      uid,
      version,
      origin: "existing",
      deleted: false,
      baseline: structuredClone(submitted),
      current: structuredClone(submitted),
    };
  });
}

/** 仅移除 feature 已确认删除成功且仍标记删除的一行，不影响未确认删除或其他草稿。 */
export function acceptDeletedEditableRow<T extends object>(
  rows: readonly EditableRow<T>[],
  clientKey: string,
): EditableRow<T>[] {
  return rows.filter(
    (row) => row.clientKey !== clientKey || row.deleted !== true,
  );
}

/** 变更按 ClientKey 对账，因此同一集合不能含有重复键，即使行身份或操作不同。 */
function assertUniqueClientKeys<T extends object>(
  rows: readonly EditableRow<T>[],
): void {
  const clientKeys = new Set<string>();
  for (const row of rows) {
    if (clientKeys.has(row.clientKey)) {
      throw new Error(`编辑集合 ClientKey 重复：${row.clientKey}`);
    }
    clientKeys.add(row.clientKey);
  }
}

/** 比较可写投影的结构值，不把独立克隆误判为变化，也不把不同原始值强制归一。 */
function areWritableValuesEqual(baseline: unknown, current: unknown): boolean {
  if (Object.is(baseline, current)) return true;
  if (baseline instanceof Date || current instanceof Date) {
    return (
      baseline instanceof Date &&
      current instanceof Date &&
      Object.is(baseline.getTime(), current.getTime())
    );
  }
  if (Array.isArray(baseline) || Array.isArray(current)) {
    return (
      Array.isArray(baseline) &&
      Array.isArray(current) &&
      baseline.length === current.length &&
      baseline.every((value, index) =>
        areWritableValuesEqual(value, current[index]),
      )
    );
  }
  if (
    baseline === null ||
    current === null ||
    typeof baseline !== "object" ||
    typeof current !== "object"
  ) {
    return false;
  }
  const baselinePrototype: unknown = Object.getPrototypeOf(baseline);
  const currentPrototype: unknown = Object.getPrototypeOf(current);
  if (
    baselinePrototype !== currentPrototype ||
    (baselinePrototype !== Object.prototype && baselinePrototype !== null)
  ) {
    // Map、Set 等非 DTO 结构须由 feature 提供明确策略，不能因没有可枚举字段误判相等。
    return false;
  }
  const baselineRecord = baseline as Record<string, unknown>;
  const currentRecord = current as Record<string, unknown>;
  const fields = Object.keys(baselineRecord);
  return (
    fields.length === Object.keys(currentRecord).length &&
    fields.every(
      (field) =>
        Object.hasOwn(currentRecord, field) &&
        areWritableValuesEqual(baselineRecord[field], currentRecord[field]),
    )
  );
}
