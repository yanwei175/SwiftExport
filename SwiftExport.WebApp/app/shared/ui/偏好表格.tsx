import { useCallback, useMemo, useRef } from "react";
import type { ColumnsState } from "@ant-design/pro-components/table";
import { useUserPreferences } from "../preferences/用户偏好上下文";
import { resolveGridPreferences } from "../preferences/用户偏好";
import type {
  GridPreferenceColumn,
  GridPreferences,
} from "../preferences/用户偏好";
import { SwiftDataGrid } from "./swift-data-grid";
import type { SwiftDataGridProps } from "./swift-data-grid";

/** 列设置直接复用 ProTable；仅把声明列布局映射到已有按用户隔离的偏好服务。 */
export function PreferenceDataGrid<T extends object>({
  preferenceId,
  ...props
}: SwiftDataGridProps<T> & { preferenceId: string }) {
  const { grids, setGridPreferences } = useUserPreferences();
  // 仅用列身份/默认宽度建立稳定依赖，字段输入和编辑器闭包变化不重建受控列状态。
  const definitionSignature = JSON.stringify(
    props.columns.map((column) => ({
      id: column.key,
      defaultWidth: typeof column.width === "number" ? column.width : undefined,
    })),
  );
  const definitions = useMemo(
    () => JSON.parse(definitionSignature) as GridPreferenceColumn[],
    [definitionSignature],
  );
  const saved = grids[preferenceId];
  const layout = useMemo(
    () => resolveGridPreferences(saved, definitions),
    [saved, definitions],
  );
  const lastPublished = useRef<{
    base: GridPreferences;
    signature: string;
  } | null>(null);
  const columns = useMemo(
    () =>
      props.columns.map((column) => ({
        ...column,
        width: layout.columnWidths[column.key] ?? column.width,
      })),
    [props.columns, layout],
  );
  const columnsValue = useMemo<Record<string, ColumnsState>>(
    () =>
      Object.fromEntries(
        layout.columnOrder.map((key, order) => [
          key,
          {
            order,
            show: !layout.hiddenColumnIds.includes(key),
            disable:
              !layout.hiddenColumnIds.includes(key) &&
              layout.hiddenColumnIds.length >= layout.columnOrder.length - 1
                ? { checkbox: true }
                : undefined,
          },
        ]),
      ),
    [layout],
  );

  /** ProTable 会回报受控状态本身；忽略序号等合成列及布局回声，避免重复发布全局偏好。 */
  const saveColumns = useCallback(
    (value: Record<string, ColumnsState>): void => {
      const order = [...layout.columnOrder].sort(
        (left, right) =>
          (value[left]?.order ?? layout.columnOrder.indexOf(left)) -
          (value[right]?.order ?? layout.columnOrder.indexOf(right)),
      );
      const next = resolveGridPreferences(
        {
          ...layout,
          columnOrder: order,
          hiddenColumnIds: order.filter((key) => value[key]?.show === false),
        },
        definitions,
      );
      if (
        next.columnOrder.every(
          (key, index) => key === layout.columnOrder[index],
        ) &&
        next.hiddenColumnIds.length === layout.hiddenColumnIds.length &&
        next.hiddenColumnIds.every((key) =>
          layout.hiddenColumnIds.includes(key),
        )
      )
        return;
      // 发布版回调位于内部 setState updater；同一更新重放不能重复通知全局 store。
      const signature = JSON.stringify([
        next.columnOrder,
        next.hiddenColumnIds,
      ]);
      if (
        lastPublished.current?.base === layout &&
        lastPublished.current.signature === signature
      )
        return;
      lastPublished.current = { base: layout, signature };
      setGridPreferences(preferenceId, next, definitions);
    },
    [layout, definitions, setGridPreferences, preferenceId],
  );
  const controlledColumns = useMemo(
    () => ({ value: columnsValue, onChange: saveColumns }),
    [columnsValue, saveColumns],
  );

  return (
    <SwiftDataGrid
      {...props}
      columns={columns}
      columnsState={controlledColumns}
      onColumnWidthChange={(key, width) => {
        if (layout.columnWidths[key] !== width)
          setGridPreferences(
            preferenceId,
            {
              ...layout,
              columnWidths: { ...layout.columnWidths, [key]: width },
            },
            definitions,
          );
        props.onColumnWidthChange?.(key, width);
      }}
    />
  );
}
