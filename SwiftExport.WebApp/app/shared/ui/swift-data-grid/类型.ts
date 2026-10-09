import type { AriaAttributes, HTMLAttributes, ReactNode } from "react";
import type {
  ColumnsState,
  ProTableProps,
} from "@ant-design/pro-components/table";

/** 行身份只用于定位和选择；序号、当前页位置与业务实体身份不互相替代。 */
export type SwiftDataGridRowKey = string | number;

/** 全部编辑器复用标签、错误与取消；业务当前值和转换始终属于调用方。 */
export interface SwiftDataGridEditorFeedback<T extends object> {
  onCancel?(row: T, index: number): void;
  getError?(row: T, index: number): string | undefined;
  getLabel(row: T, index: number): string;
}

/** 自定义控件接收一致的只读与无障碍上下文，不获得业务状态或 HTTP 能力。 */
export interface SwiftDataGridEditorContext<T extends object> {
  row: T;
  index: number;
  readOnly: boolean;
  error?: string;
  inputAttributes: AriaAttributes & { id: string };
  cancel(): void;
}

/** 文本值、原始缓冲、转换与错误均由调用方拥有，Grid 只显示并转交输入意图。 */
export interface SwiftDataGridTextEditor<
  T extends object,
> extends SwiftDataGridEditorFeedback<T> {
  kind?: "text";
  getText(row: T, index: number): string;
  onChange(row: T, text: string, index: number): void;
  inputMode?: HTMLAttributes<HTMLInputElement>["inputMode"];
}

/** 枚举、日期及候选通过列内受控组合实现，不使用编辑器注册表或插件协议。 */
export interface SwiftDataGridCustomEditor<
  T extends object,
> extends SwiftDataGridEditorFeedback<T> {
  kind: "custom";
  render(context: SwiftDataGridEditorContext<T>): ReactNode;
}

/** 列 key 保持稳定；自定义展示和受控文本编辑只选择其中一种。 */
export type SwiftDataGridColumn<T extends object> = {
  key: string;
  title: ReactNode;
  width?: string | number;
  readOnly?: boolean;
  /** 文本列默认允许填充，自定义列须显式允许，业务仍自行校验接纳。 */
  allowFillDown?: boolean;
  /** 关联业务字段标识可包含未展示字段；默认 [key]，不解析路径或授权字段写入。 */
  fillDownFields?: readonly string[];
} & (
  | {
      editor: SwiftDataGridTextEditor<T> | SwiftDataGridCustomEditor<T>;
      render?: never;
    }
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
  /** 每列使用矩形顶部作为源，单次交给 feature 统一校验与提交编辑意图。 */
  columns: readonly {
    columnKey: string;
    /** 当前列声明的关联字段；feature 复核映射、字段写权并统一更新，Grid 不反射读写。 */
    fields: readonly string[];
    source: T;
    targets: readonly T[];
  }[];
}

/** 公共表格组合 ProTable 与受控编辑器；草稿、校验、替换保护和保存由 feature 负责。 */
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
  loading?: boolean;
  /** 占满调用方提供的有界高度，表头、工具栏及分页固定，仅表格正文内部滚动；默认自然高度。 */
  fillHeight?: boolean;
  /** 列设置只接受受控布局，不启用 ProTable 的第二套 Storage 持久化。 */
  columnsState?: {
    value: Record<string, ColumnsState>;
    onChange(value: Record<string, ColumnsState>): void;
  };
  onColumnWidthChange?(columnKey: string, width: number): void;
  /** 调用方可以复用公开工具栏插槽，不覆盖业务查询、编辑和保存入口。 */
  toolBarRender?: Exclude<
    ProTableProps<T, Record<string, unknown>>["toolBarRender"],
    false
  >;
}
