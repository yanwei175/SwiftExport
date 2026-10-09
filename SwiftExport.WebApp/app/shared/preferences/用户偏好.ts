import { recordDiagnostic } from "../diagnostics/诊断记录";

/** 分页大小只提供当前统一选项，不保存筛选、搜索、选中实体或页签身份。 */
export type PreferencePageSize = 20 | 50 | 100;

/** 调用方声明当前可用列；未知旧列被移除，不能用偏好恢复失权字段。 */
export interface GridPreferenceColumn {
  readonly id: string;
  readonly defaultWidth?: number;
  readonly hideable?: boolean;
}

/** 只保存界面列布局和分页大小，不保存任何行、单元格或业务 DTO。 */
export interface GridPreferences {
  readonly columnOrder: readonly string[];
  readonly hiddenColumnIds: readonly string[];
  readonly columnWidths: Readonly<Record<string, number>>;
  readonly pageSize: PreferencePageSize;
}

/** 当前用户的只读偏好快照；未加载状态避免首次渲染伪称持久化可用。 */
export interface UserPreferenceSnapshot {
  readonly sidebarCollapsed: boolean;
  readonly grids: Readonly<Record<string, GridPreferences>>;
  readonly storageStatus: "not-loaded" | "persistent" | "memory-only";
}

/** 单一应用上下文持有本 store；load 由浏览器 effect 调用，创建时不访问 window。 */
export interface UserPreferenceStore {
  readonly getSnapshot: () => UserPreferenceSnapshot;
  readonly subscribe: (listener: () => void) => () => void;
  readonly load: () => void;
  readonly setSidebarCollapsed: (collapsed: boolean) => void;
  readonly setGridPreferences: (
    gridId: string,
    preferences: GridPreferences,
    columns: readonly GridPreferenceColumn[],
  ) => void;
  readonly resetPreferences: () => void;
}

/** 判断可供白名单解析的普通对象，不将未知属性透传到记录。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** 列标识去重；不解析标识为业务内容，也不读取原始异常信息。 */
function columnIds(value: unknown): string[] {
  return Array.isArray(value)
    ? [
        ...new Set(
          value.filter(
            (id): id is string => typeof id === "string" && Boolean(id),
          ),
        ),
      ]
    : [];
}

/** 只接受有限正宽度，损坏数字回退到当前列定义。 */
function validWidth(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value > 0;
}

/** 从持久化对象提取明确的列布局字段，忽略其他字段和非法分页选项。 */
function readGridPreferences(value: unknown): GridPreferences {
  const input = isRecord(value) ? value : {};
  const widths = isRecord(input.columnWidths) ? input.columnWidths : {};
  return Object.freeze({
    columnOrder: Object.freeze(columnIds(input.columnOrder)),
    hiddenColumnIds: Object.freeze(columnIds(input.hiddenColumnIds)),
    columnWidths: Object.freeze(
      Object.fromEntries(
        Object.entries(widths).filter((entry): entry is [string, number] =>
          validWidth(entry[1]),
        ),
      ),
    ),
    pageSize:
      input.pageSize === 20 || input.pageSize === 100 ? input.pageSize : 50,
  });
}

/** 当前声明列决定可见范围与默认值；不能隐藏受保护列或所有数据列。 */
export function resolveGridPreferences(
  preferences: GridPreferences | undefined,
  columns: readonly GridPreferenceColumn[],
): GridPreferences {
  const input = readGridPreferences(preferences);
  const definitions = [
    ...new Map(columns.map((column) => [column.id, column])).values(),
  ];
  const ids = definitions.map((column) => column.id);
  const allowed = new Set(ids);
  const order = input.columnOrder.filter((id) => allowed.has(id));
  const ordered = new Set(order);
  order.push(...ids.filter((id) => !ordered.has(id)));
  let hidden = input.hiddenColumnIds.filter(
    (id) =>
      allowed.has(id) &&
      definitions.find((column) => column.id === id)?.hideable !== false,
  );
  if (ids.length && hidden.length === ids.length)
    hidden = hidden.filter((id) => id !== order[0]);
  const widths = Object.fromEntries(
    definitions.flatMap((column) => {
      const width = input.columnWidths[column.id] ?? column.defaultWidth;
      return validWidth(width) ? [[column.id, width]] : [];
    }),
  );
  return Object.freeze({
    columnOrder: Object.freeze(order),
    hiddenColumnIds: Object.freeze(hidden),
    columnWidths: Object.freeze(widths),
    pageSize: input.pageSize,
  });
}

/** 创建当前身份的界面偏好；匿名状态只有默认内存值，不读写其他用户键。 */
export function createUserPreferenceStore(
  userId: string | null,
): UserPreferenceStore {
  const storageKey = userId?.trim()
    ? `swiftexport:web:prefs:v1:${encodeURIComponent(userId)}`
    : null;
  let snapshot: UserPreferenceSnapshot = Object.freeze({
    sidebarCollapsed: false,
    grids: Object.freeze({}),
    storageStatus: "not-loaded",
  });
  let loaded = false;
  const subscribers = new Set<() => void>();

  /** 发布不可变快照，Storage 失败也保留当前操作产生的界面状态。 */
  function publish(next: UserPreferenceSnapshot): void {
    snapshot = Object.freeze(next);
    for (const subscriber of subscribers) subscriber();
  }

  /** 失败只记录固定类别，绝不把损坏原文或浏览器异常转储到日志。 */
  function memoryOnly(kind: "storage-unavailable" | "invalid-record"): void {
    publish({ ...snapshot, storageStatus: "memory-only" });
    recordDiagnostic({
      event: "preference-persist-failed",
      level: "warn",
      kind,
    });
  }

  /** 仅持久化当前用户的白名单值；无可用存储时仍保持页面内存状态。 */
  function persist(): void {
    if (!storageKey || typeof window === "undefined") {
      publish({ ...snapshot, storageStatus: "memory-only" });
      return;
    }
    try {
      window.localStorage.setItem(
        storageKey,
        JSON.stringify({
          schemaVersion: 1,
          sidebarCollapsed: snapshot.sidebarCollapsed,
          grids: snapshot.grids,
        }),
      );
      publish({ ...snapshot, storageStatus: "persistent" });
    } catch {
      memoryOnly("storage-unavailable");
    }
  }

  const store: UserPreferenceStore = {
    getSnapshot: () => snapshot,
    subscribe: (listener) => {
      subscribers.add(listener);
      return () => subscribers.delete(listener);
    },
    load: () => {
      if (loaded) return;
      loaded = true;
      if (!storageKey || typeof window === "undefined") {
        publish({ ...snapshot, storageStatus: "memory-only" });
        return;
      }
      try {
        const raw = window.localStorage.getItem(storageKey);
        if (!raw) {
          publish({ ...snapshot, storageStatus: "persistent" });
          return;
        }
        let record: unknown;
        try {
          record = JSON.parse(raw);
          if (!isRecord(record) || record.schemaVersion !== 1)
            throw new Error();
        } catch {
          // 只移除当前偏好键；损坏资料不能影响别的身份或应用存储。
          window.localStorage.removeItem(storageKey);
          publish({ ...snapshot, storageStatus: "persistent" });
          recordDiagnostic({
            event: "preference-persist-failed",
            level: "warn",
            kind: "invalid-record",
          });
          return;
        }
        const grids = isRecord(record.grids) ? record.grids : {};
        publish({
          sidebarCollapsed: record.sidebarCollapsed === true,
          grids: Object.freeze(
            Object.fromEntries(
              Object.entries(grids).map(([id, layout]) => [
                id,
                readGridPreferences(layout),
              ]),
            ),
          ),
          storageStatus: "persistent",
        });
      } catch {
        memoryOnly("storage-unavailable");
      }
    },
    setSidebarCollapsed: (collapsed) => {
      store.load();
      publish({ ...snapshot, sidebarCollapsed: collapsed === true });
      persist();
    },
    setGridPreferences: (gridId, preferences, columns) => {
      if (!gridId.trim()) throw new Error("界面偏好需要声明稳定 Grid 标识。");
      store.load();
      publish({
        ...snapshot,
        grids: Object.freeze({
          ...snapshot.grids,
          [gridId]: resolveGridPreferences(preferences, columns),
        }),
      });
      persist();
    },
    resetPreferences: () => {
      store.load();
      publish({
        ...snapshot,
        sidebarCollapsed: false,
        grids: Object.freeze({}),
      });
      if (!storageKey || typeof window === "undefined") return;
      try {
        window.localStorage.removeItem(storageKey);
        publish({ ...snapshot, storageStatus: "persistent" });
      } catch {
        memoryOnly("storage-unavailable");
      }
    },
  };
  return store;
}
