import { useId, useState } from "react";
import type { KeyboardEvent } from "react";
import { Button, Input, Space, Table, Typography } from "antd";
import type { TableColumnsType } from "antd";
import type {
  SwiftDataGridColumn,
  SwiftDataGridFillDownIntent,
  SwiftDataGridProps,
  SwiftDataGridRowKey,
} from "./类型";

/** 活动单元格仅决定填充列和焦点提示，不包含业务当前值或编辑缓冲。 */
interface ActiveCell {
  rowKey: SwiftDataGridRowKey;
  columnKey: string;
}

/** 输入控件保留自己的快捷键；Grid 不抢输入框或可编辑文本内的 Ctrl+D。 */
function isTextInput(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    (Boolean(target.closest("input, textarea")) || target.isContentEditable)
  );
}

/** 受控公共表格只产生编辑、选择、分页和填充意图，不缓存草稿或提交业务请求。 */
export function SwiftDataGrid<T extends object>({
  rows,
  getRowKey,
  columns,
  readOnly = false,
  canEdit,
  selectedKeys,
  onSelectionChange,
  onFillDown,
  showRowNumber = true,
  pagination,
}: SwiftDataGridProps<T>) {
  const gridId = useId();
  const [activeCell, setActiveCell] = useState<ActiveCell | null>(null);

  /** 当前授权只用于界面编辑意图；调用方仍须核对最新权限与业务保存锁。 */
  function isEditable(row: T, column: SwiftDataGridColumn<T>): boolean {
    return (
      !readOnly &&
      !column.readOnly &&
      Boolean(column.editor) &&
      (canEdit?.(row, column.key) ?? true)
    );
  }

  /** 选择顺序不改变业务顺序；跨页选择不能成为本页填充目标。 */
  function getFillIntent(): SwiftDataGridFillDownIntent<T> | null {
    if (!onFillDown || readOnly || !activeCell) return null;
    const column = columns.find((item) => item.key === activeCell.columnKey);
    if (!column?.editor || column.readOnly) return null;
    if (!rows.some((row) => getRowKey(row) === activeCell.rowKey)) return null;
    const selected = new Set(selectedKeys);
    const visibleSelection = rows.filter((row) => selected.has(getRowKey(row)));
    const source = visibleSelection[0];
    if (!source) return null;
    const targets = visibleSelection
      .slice(1)
      .filter((row) => isEditable(row, column));
    return targets.length ? { columnKey: column.key, source, targets } : null;
  }

  /** 按最新受控 props 重新核对目标，按钮和快捷键复用同一个填充入口。 */
  function requestFill(): boolean {
    const intent = getFillIntent();
    if (!intent || !onFillDown) return false;
    onFillDown(intent);
    return true;
  }

  /** 仅在 Grid 内存在有效填充意图时阻止浏览器 Ctrl+D 默认行为。 */
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (
      event.ctrlKey &&
      !event.shiftKey &&
      !event.altKey &&
      !event.metaKey &&
      event.key.toLowerCase() === "d" &&
      !isTextInput(event.target) &&
      requestFill()
    ) {
      event.preventDefault();
    }
  }

  const tableColumns: TableColumnsType<T> = columns.map((column) => ({
    key: column.key,
    title: column.title,
    width: column.width,
    onCell: (row) => {
      const rowKey = getRowKey(row);
      const active =
        activeCell?.rowKey === rowKey && activeCell.columnKey === column.key;
      return {
        tabIndex: 0,
        "data-column-key": column.key,
        onClick: () => setActiveCell({ rowKey, columnKey: column.key }),
        onFocus: () => setActiveCell({ rowKey, columnKey: column.key }),
        style: active ? { boxShadow: "inset 0 0 0 1px #176d88" } : undefined,
      };
    },
    render: (_value: unknown, row: T, index: number) => {
      if (!column.editor) return column.render(row, index);
      const editor = column.editor;
      const error = editor.getError?.(row, index);
      const editable = isEditable(row, column);
      const errorId = `${gridId}-${encodeURIComponent(String(getRowKey(row)))}-${encodeURIComponent(column.key)}-error`;
      return (
        <div>
          <Input
            aria-label={editor.getLabel(row, index)}
            aria-invalid={Boolean(error)}
            aria-describedby={error ? errorId : undefined}
            value={editor.getText(row, index)}
            inputMode={editor.inputMode}
            readOnly={!editable}
            status={error ? "error" : undefined}
            onChange={(event) => {
              if (editable) editor.onChange(row, event.target.value, index);
            }}
            onKeyDown={(event) => {
              if (event.key === "Escape" && editable && editor.onCancel) {
                event.preventDefault();
                editor.onCancel(row, index);
              }
            }}
          />
          {error && (
            <Typography.Text id={errorId} type="danger" role="alert">
              {error}
            </Typography.Text>
          )}
        </div>
      );
    },
  }));
  if (showRowNumber) {
    tableColumns.unshift({
      key: "__swift-row-number",
      title: "序号",
      width: 72,
      render: (_value: unknown, _row: T, index: number) =>
        ((pagination?.pageIndex ?? 1) - 1) *
          (pagination?.pageSize ?? rows.length) +
        index +
        1,
    });
  }

  return (
    <div role="group" aria-label="数据表格" onKeyDown={handleKeyDown}>
      <Space orientation="vertical" style={{ width: "100%" }}>
        <Button disabled={!getFillIntent()} onClick={() => requestFill()}>
          向下填充
        </Button>
        <Table<T>
          rowKey={getRowKey}
          dataSource={[...rows]}
          columns={tableColumns}
          size="small"
          rowSelection={{
            selectedRowKeys: [...selectedKeys],
            preserveSelectedRowKeys: true,
            getCheckboxProps: (row) => ({
              "aria-label": `选择行 ${getRowKey(row)}`,
            }),
            getTitleCheckboxProps: () => ({
              "aria-label": "选择当前页全部行",
            }),
            onChange: (keys) =>
              onSelectionChange(
                keys.filter(
                  (key): key is SwiftDataGridRowKey =>
                    typeof key === "string" || typeof key === "number",
                ),
              ),
          }}
          pagination={
            pagination
              ? {
                  current: pagination.pageIndex,
                  pageSize: pagination.pageSize,
                  total: pagination.total,
                  showSizeChanger: false,
                  onChange: pagination.onChange,
                }
              : false
          }
        />
      </Space>
    </div>
  );
}
