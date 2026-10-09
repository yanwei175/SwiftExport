import { useId, useRef, useState } from "react";
import type { KeyboardEvent, PointerEvent, ReactNode } from "react";
import { Button, Input, Tooltip, Typography } from "antd";
import { ProTable } from "@ant-design/pro-components/table/core";
import type { ProColumns } from "@ant-design/pro-components/table/core";
import {
  buildGridFillDownIntent,
  constrainGridColumnWidth,
  gridLayoutSignature,
  moveGridCell,
  resolveGridCellRange,
} from "./单元格区域";
import type { GridCell, GridCellSelection } from "./单元格区域";
import type {
  SwiftDataGridColumn,
  SwiftDataGridFillDownIntent,
  SwiftDataGridProps,
  SwiftDataGridRowKey,
} from "./类型";
import { ColumnResizeHandle } from "./列宽调整";
import "./表格样式.css";

/** 输入控件保留自身鼠标和键盘行为；表格只在单元格空白拖选区域。 */
function isInteractive(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    Boolean(
      target.closest(
        "input, textarea, button, a, [contenteditable=true], .ant-select, .ant-picker, [role=separator]",
      ),
    )
  );
}

/** 受控 ProTable 只产生布局、选择和区域填充意图；业务值和原始缓冲仍归 feature。 */
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
  loading = false,
  columnsState,
  onColumnWidthChange,
  toolBarRender,
}: SwiftDataGridProps<T>) {
  const gridId = useId();
  const container = useRef<HTMLDivElement>(null);
  const [selection, setSelection] = useState<GridCellSelection | null>(null);
  const [localWidths, setLocalWidths] = useState<
    Record<string, { base: number; width: number }>
  >({});
  const selectionDrag = useRef<{
    pointerId: number;
    anchor: GridCell;
    startX: number;
    startY: number;
    moved: boolean;
  } | null>(null);
  const suppressClick = useRef(false);
  const visibleColumns = [...columns]
    .filter((column) => columnsState?.value[column.key]?.show !== false)
    .sort(
      (left, right) =>
        (columnsState?.value[left.key]?.order ?? columns.indexOf(left)) -
        (columnsState?.value[right.key]?.order ?? columns.indexOf(right)),
    );
  const rowKeys = rows.map(getRowKey);
  const columnKeys = visibleColumns.map((column) => column.key);
  const layout = gridLayoutSignature(
    rowKeys,
    columnKeys,
    pagination?.pageIndex,
    pagination?.pageSize,
  );
  const range = resolveGridCellRange(selection, rowKeys, columnKeys, layout);
  // 同一组件在布局变动时清理纯 UI 选择，防止恢复旧页码/列布局后旧矩形再次出现。
  if (selection && selection.layout !== layout) setSelection(null);

  /** 可见顺序或分页变化让旧区域立即失效，不把坐标继承给其他业务行。 */
  function selectCell(cell: GridCell, extend: boolean): void {
    setSelection((current) => ({
      anchor: extend && current?.layout === layout ? current.anchor : cell,
      end: cell,
      layout,
    }));
  }
  /** 调用方仍须在保存入口复核最新业务权限和提交锁。 */
  function isEditable(row: T, column: SwiftDataGridColumn<T>): boolean {
    return (
      !readOnly &&
      !loading &&
      !column.readOnly &&
      Boolean(column.editor) &&
      (canEdit?.(row, column.key) ?? true)
    );
  }
  /** 每列顶部是源，下方仅包含矩形内的可写目标；行勾选不参与填充。 */
  function getFillIntent(): SwiftDataGridFillDownIntent<T> | null {
    if (
      !onFillDown ||
      readOnly ||
      loading ||
      !range ||
      range.firstRow === range.lastRow
    )
      return null;
    return buildGridFillDownIntent(
      range,
      rows,
      columnKeys,
      (key) => {
        const column = visibleColumns.find((item) => item.key === key);
        return Boolean(
          column?.editor &&
          (column.allowFillDown ?? column.editor.kind !== "custom"),
        );
      },
      (row, key) => {
        const column = visibleColumns.find((item) => item.key === key);
        return Boolean(column && isEditable(row, column));
      },
    );
  }
  /** 按最新 props 重算区域意图，不转换字段、不改变集合或基线。 */
  function requestFill(): boolean {
    const intent = getFillIntent();
    if (!intent || !onFillDown) return false;
    onFillDown(intent);
    return true;
  }
  /** 焦点在单元格本身时方向键移动，Shift 扩区；不抢编辑器内部按键。 */
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.defaultPrevented || isInteractive(event.target)) return;
    if (
      event.ctrlKey &&
      !event.shiftKey &&
      !event.altKey &&
      !event.metaKey &&
      event.key.toLowerCase() === "d" &&
      requestFill()
    ) {
      event.preventDefault();
      return;
    }
    const offsets: Record<string, readonly [number, number]> = {
      ArrowUp: [-1, 0],
      ArrowDown: [1, 0],
      ArrowLeft: [0, -1],
      ArrowRight: [0, 1],
    };
    const offset = offsets[event.key];
    if (
      !offset ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      !selection ||
      !range
    )
      return;
    const next = moveGridCell(
      selection.end,
      rowKeys,
      columnKeys,
      offset[0],
      offset[1],
    );
    if (!next) return;
    event.preventDefault();
    selectCell(next, event.shiftKey);
    const cells = container.current?.querySelectorAll<HTMLElement>(
      "td[data-column-key]",
    );
    Array.from(cells ?? [])
      .find(
        (item) =>
          item.dataset.columnKey === next.columnKey &&
          item.dataset.swiftRowKey === JSON.stringify(next.rowKey),
      )
      ?.focus();
  }
  /** 指针捕获后按位置查找当前表格单元格，不把浮层或其他表格选进区域。 */
  function handlePointerMove(event: PointerEvent<HTMLDivElement>): void {
    const current = selectionDrag.current;
    if (!current || current.pointerId !== event.pointerId) return;
    if (
      !current.moved &&
      Math.hypot(
        event.clientX - current.startX,
        event.clientY - current.startY,
      ) < 4
    )
      return;
    const cell = document
      .elementFromPoint(event.clientX, event.clientY)
      ?.closest<HTMLElement>("td[data-column-key]");
    if (!cell || !container.current?.contains(cell)) return;
    const rowKey = rowKeys.find(
      (key) => JSON.stringify(key) === cell.dataset.swiftRowKey,
    );
    const columnKey = cell.dataset.columnKey;
    if (rowKey === undefined || !columnKey || !columnKeys.includes(columnKey))
      return;
    current.moved = true;
    suppressClick.current = true;
    event.preventDefault();
    setSelection({
      anchor: current.anchor,
      end: { rowKey, columnKey },
      layout,
    });
  }
  /** 拖动只覆盖同一显示宽度基线；外部偏好更新直接成为新宽度。 */
  function widthFor(column: SwiftDataGridColumn<T>): number {
    const base = constrainGridColumnWidth(
      typeof column.width === "number"
        ? column.width
        : Number.parseFloat(column.width ?? "160"),
    );
    const local = localWidths[column.key];
    return local?.base === base ? local.width : base;
  }
  /** 受控编辑器沿用错误、只读和取消契约，不启用第二套 editable Form 状态。 */
  function renderCell(
    column: SwiftDataGridColumn<T>,
    row: T,
    index: number,
  ): ReactNode {
    if (!column.editor) {
      const rendered = column.render(row, index);
      return (
        <Tooltip
          title={
            typeof rendered === "string" || typeof rendered === "number"
              ? rendered
              : undefined
          }
        >
          <div className="swift-grid-display">{rendered}</div>
        </Tooltip>
      );
    }
    const editor = column.editor;
    const error = editor.getError?.(row, index);
    const editable = isEditable(row, column);
    const errorId = `${gridId}-${encodeURIComponent(String(getRowKey(row)))}-${encodeURIComponent(column.key)}-error`;
    const inputAttributes = {
      id: `${errorId}-input`,
      "aria-label": editor.getLabel(row, index),
      "aria-invalid": Boolean(error),
      "aria-describedby": error ? errorId : undefined,
    } as const;
    const cancel = () => {
      if (editable) editor.onCancel?.(row, index);
    };
    return (
      <div
        className="swift-grid-editor"
        data-readonly={!editable}
        onKeyDown={(event) => {
          // 自定义 Select/日期先处理自己的 Esc；文本 Esc 仅取消该字段原始缓冲。
          if (
            event.key === "Escape" &&
            !event.defaultPrevented &&
            editable &&
            editor.onCancel &&
            editor.kind !== "custom"
          ) {
            event.preventDefault();
            cancel();
          }
        }}
      >
        {editor.kind === "custom" ? (
          editor.render({
            row,
            index,
            readOnly: !editable,
            error,
            inputAttributes,
            cancel,
          })
        ) : (
          <Input
            {...inputAttributes}
            value={editor.getText(row, index)}
            inputMode={editor.inputMode}
            readOnly={!editable}
            status={error ? "error" : undefined}
            onChange={(event) => {
              if (editable) editor.onChange(row, event.target.value, index);
            }}
          />
        )}
        {error && (
          <Typography.Text id={errorId} type="danger" role="alert">
            {error}
          </Typography.Text>
        )}
      </div>
    );
  }
  const tableColumns: ProColumns<T>[] = columns.map((column) => {
    const width = widthFor(column);
    const label = typeof column.title === "string" ? column.title : column.key;
    return {
      key: column.key,
      title: (
        <>
          <span className="swift-grid-header-title">{column.title}</span>
          <ColumnResizeHandle
            label={label}
            width={width}
            onResize={(next) => {
              const base = constrainGridColumnWidth(
                typeof column.width === "number"
                  ? column.width
                  : Number.parseFloat(column.width ?? "160"),
              );
              setLocalWidths((current) => ({
                ...current,
                [column.key]: { base, width: next },
              }));
            }}
            onFinish={(next) => onColumnWidthChange?.(column.key, next)}
          />
        </>
      ),
      width,
      ellipsis: true,
      search: false,
      onHeaderCell: () => ({
        className: "swift-grid-header",
        "data-column-key": column.key,
      }),
      onCell: (row) => {
        const rowKey = getRowKey(row);
        const rowIndex = rowKeys.indexOf(rowKey);
        const columnIndex = columnKeys.indexOf(column.key);
        const selected = Boolean(
          range &&
          rowIndex >= range.firstRow &&
          rowIndex <= range.lastRow &&
          columnIndex >= range.firstColumn &&
          columnIndex <= range.lastColumn,
        );
        return {
          tabIndex: 0,
          "aria-selected": selected,
          "data-selected": selected,
          "data-column-key": column.key,
          "data-swift-row-key": JSON.stringify(rowKey),
          className: selected
            ? "swift-grid-cell swift-grid-cell-selected"
            : "swift-grid-cell",
          onClick: (event) => {
            if (suppressClick.current) {
              suppressClick.current = false;
              return;
            }
            selectCell({ rowKey, columnKey: column.key }, event.shiftKey);
          },
          onPointerDown: (event) => {
            suppressClick.current = false;
            if (event.button !== 0 || isInteractive(event.target)) return;
            const cell = { rowKey, columnKey: column.key };
            const anchor =
              event.shiftKey && selection?.layout === layout
                ? selection.anchor
                : cell;
            setSelection({ anchor, end: cell, layout });
            selectionDrag.current = {
              pointerId: event.pointerId,
              anchor,
              startX: event.clientX,
              startY: event.clientY,
              moved: false,
            };
            container.current?.setPointerCapture(event.pointerId);
            event.currentTarget.focus();
            event.preventDefault();
          },
        };
      },
      render: (_value, row, index) => renderCell(column, row, index),
    };
  });
  if (showRowNumber)
    tableColumns.unshift({
      key: "__swift-row-number",
      title: "序号",
      width: 72,
      hideInSetting: true,
      search: false,
      render: (_value, _row, index) =>
        ((pagination?.pageIndex ?? 1) - 1) *
          (pagination?.pageSize ?? rows.length) +
        index +
        1,
    });
  return (
    <div
      ref={container}
      className="swift-data-grid"
      role="group"
      aria-label="数据表格"
      onKeyDown={handleKeyDown}
      onPointerMove={handlePointerMove}
      onPointerUp={(event) => {
        selectionDrag.current = null;
        if (event.currentTarget.hasPointerCapture(event.pointerId))
          event.currentTarget.releasePointerCapture(event.pointerId);
      }}
      onPointerCancel={() => {
        selectionDrag.current = null;
      }}
      onLostPointerCapture={() => {
        selectionDrag.current = null;
      }}
    >
      <ProTable<T, Record<string, unknown>>
        rowKey={getRowKey}
        dataSource={[...rows]}
        columns={tableColumns}
        loading={loading}
        size="small"
        tableLayout="fixed"
        scroll={{
          x: visibleColumns.reduce(
            (sum, column) => sum + widthFor(column),
            showRowNumber ? 120 : 48,
          ),
        }}
        search={false}
        cardBordered={false}
        cardProps={false}
        tableAlertRender={false}
        columnsState={columnsState}
        options={{
          density: false,
          reload: false,
          fullScreen: false,
          setting: columnsState
            ? {
                draggable: true,
                showListItemOption: false,
                listItemTitleRender: (_title, item) =>
                  columns.find((column) => column.key === item.key)?.title ??
                  _title,
                children: (
                  <Button size="small" aria-label="表格显示设置">
                    表格显示设置
                  </Button>
                ),
              }
            : false,
        }}
        toolBarRender={(action, selected) => [
          <Button
            key="fill"
            disabled={!getFillIntent()}
            onClick={() => requestFill()}
          >
            向下填充
          </Button>,
          ...(toolBarRender?.(action, selected) || []),
        ]}
        rowSelection={{
          columnWidth: 48,
          selectedRowKeys: [...selectedKeys],
          preserveSelectedRowKeys: true,
          getCheckboxProps: (row) => ({
            "aria-label": `选择行 ${getRowKey(row)}`,
          }),
          getTitleCheckboxProps: () => ({ "aria-label": "选择当前页全部行" }),
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
    </div>
  );
}
