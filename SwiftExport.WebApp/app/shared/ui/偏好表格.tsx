import { Button, Checkbox, InputNumber, Modal, Space, Typography } from "antd";
import { useState } from "react";
import { useUserPreferences } from "../preferences/用户偏好上下文";
import { resolveGridPreferences } from "../preferences/用户偏好";
import type { GridPreferences } from "../preferences/用户偏好";
import { SwiftDataGrid } from "./swift-data-grid";
import type { SwiftDataGridProps } from "./swift-data-grid";

/** 偏好适配只调整显示列；业务 rows、分页、选择及编辑仍由 feature 受控。 */
export function PreferenceDataGrid<T extends object>({
  preferenceId,
  ...props
}: SwiftDataGridProps<T> & { preferenceId: string }) {
  const preferences = useUserPreferences();
  const [open, setOpen] = useState(false);
  const definitions = props.columns.map((column) => ({
    id: column.key,
    defaultWidth: typeof column.width === "number" ? column.width : undefined,
  }));
  const layout = resolveGridPreferences(
    preferences.grids[preferenceId],
    definitions,
  );
  const visibleColumns = layout.columnOrder
    .filter((key) => !layout.hiddenColumnIds.includes(key))
    .map((key) => {
      const column = props.columns.find((item) => item.key === key)!;
      return { ...column, width: layout.columnWidths[key] ?? column.width };
    });
  /** 当前声明列是白名单，不让持久偏好重新引入失权字段。 */
  function saveLayout(next: GridPreferences): void {
    preferences.setGridPreferences(preferenceId, next, definitions);
  }
  /** 调整顺序只改变显示，绝不重排业务行或修改输入。 */
  function moveColumn(index: number, offset: number): void {
    const order = [...layout.columnOrder];
    const destination = index + offset;
    if (destination < 0 || destination >= order.length) return;
    [order[index], order[destination]] = [order[destination]!, order[index]!];
    saveLayout({ ...layout, columnOrder: order });
  }
  return (
    <Space orientation="vertical" style={{ width: "100%" }}>
      <Button onClick={() => setOpen(true)}>表格显示设置</Button>
      <SwiftDataGrid {...props} columns={visibleColumns} />
      <Modal
        title="表格显示设置"
        open={open}
        onCancel={() => setOpen(false)}
        footer={<Button onClick={() => setOpen(false)}>完成</Button>}
      >
        <Typography.Paragraph type="secondary">
          仅保存列布局，不影响业务输入。
        </Typography.Paragraph>
        <Space orientation="vertical" style={{ width: "100%" }}>
          {layout.columnOrder.map((key, index) => {
            const column = props.columns.find((item) => item.key === key)!;
            const visible = !layout.hiddenColumnIds.includes(key);
            return (
              <Space key={key} wrap>
                <Checkbox
                  aria-label={`显示列 ${key}`}
                  checked={visible}
                  disabled={visible && visibleColumns.length <= 1}
                  onChange={(event) =>
                    saveLayout({
                      ...layout,
                      hiddenColumnIds: event.target.checked
                        ? layout.hiddenColumnIds.filter((id) => id !== key)
                        : [...layout.hiddenColumnIds, key],
                    })
                  }
                >
                  {column.title}
                </Checkbox>
                <InputNumber
                  aria-label={`列宽 ${key}`}
                  min={48}
                  max={1000}
                  value={layout.columnWidths[key]}
                  onChange={(value) => {
                    if (typeof value === "number")
                      saveLayout({
                        ...layout,
                        columnWidths: { ...layout.columnWidths, [key]: value },
                      });
                  }}
                />
                <Button
                  aria-label={`前移列 ${key}`}
                  disabled={index === 0}
                  onClick={() => moveColumn(index, -1)}
                >
                  前移
                </Button>
                <Button
                  aria-label={`后移列 ${key}`}
                  disabled={index === layout.columnOrder.length - 1}
                  onClick={() => moveColumn(index, 1)}
                >
                  后移
                </Button>
              </Space>
            );
          })}
          <Button
            onClick={() =>
              saveLayout(resolveGridPreferences(undefined, definitions))
            }
          >
            恢复默认列布局
          </Button>
        </Space>
      </Modal>
    </Space>
  );
}
