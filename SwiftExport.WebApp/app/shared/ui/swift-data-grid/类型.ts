import type { HTMLAttributes, ReactNode } from "react";

/** 行身份只用于定位和选择；序号、当前页位置与业务实体身份不互相替代。 */
export type SwiftDataGridRowKey = string | number;

/** 文本值、原始缓冲、转换与错误均由调用方拥有，Grid 只显示并转交输入意图。 */
export interface SwiftDataGridTextEditor<T extends object> {
  getText(row: T, index: number): string;
  onChange(row: T, text: string, index: number): void;
  onCancel?(row: T, index: number): void;
  getError?(row: T, index: number): string | undefined;
  getLabel(row: T, index: number): string;
  inputMode?: HTMLAttributes<HTMLInputElement>["inputMode"];
}

/** 列 key 保持稳定；自定义展示和受控文本编辑只选择其中一种。 */
export type SwiftDataGridColumn<T extends object> = {
  key: string;
  title: ReactNode;
  width?: string | number;
  readOnly?: boolean;
} & (
  | { editor: SwiftDataGridTextEditor<T>; render?: never }
  | { render(row: T, index: number): ReactNode; editor?: never }
);

/** 分页完全受控；调用方先处理当前页输入保护，再更新 rows 与页码。 */
export interface SwiftDataGridPagination {
  pageIndex: number;
  pageSize: number;
  total: number;
  onChange(pageIndex: number, pageSize: number): void;
}

/** 向下填充仅传递可见顺序中的源与可写目标，不转换字段、不写入集合或后端。 */
export interface SwiftDataGridFillDownIntent<T extends object> {
  columnKey: string;
  source: T;
  targets: readonly T[];
}

/** 公共表格只组合 Ant Table/Input；草稿、校验、替换保护与保存由 feature 负责。 */
export interface SwiftDataGridProps<T extends object> {
  rows: readonly T[];
  getRowKey(row: T): SwiftDataGridRowKey;
  columns: readonly SwiftDataGridColumn<T>[];
  readOnly?: boolean;
  canEdit?(row: T, columnKey: string): boolean;
  selectedKeys: readonly SwiftDataGridRowKey[];
  onSelectionChange(keys: SwiftDataGridRowKey[]): void;
  onFillDown?(intent: SwiftDataGridFillDownIntent<T>): void;
  showRowNumber?: boolean;
  pagination?: SwiftDataGridPagination;
}
