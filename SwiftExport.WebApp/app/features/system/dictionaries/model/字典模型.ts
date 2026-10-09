import type {
  SwiftExportContractsDTOsSystemDtosSysDictTypeDto,
  SwiftExportContractsDTOsSystemDtosSysDictDataDto,
} from "../../../../shared/api/generated/client";
import {
  createEditableRow,
  createCreatedEditableRow,
  updateEditableRow,
  removeEditableRow,
  restoreEditableRow,
  discardEditableRows,
} from "../../../../shared/editing/编辑集合";
import type { EditableRow } from "../../../../shared/editing/编辑集合";
import type { ParsedInput } from "../../../../shared/editing/未提交输入";
import { parseIntegerText } from "../../../../shared/format/数值转换";

/** 字典页面直接消费 Orval DTO，不维护第二套传输模型。 */
export type DictionaryType = SwiftExportContractsDTOsSystemDtosSysDictTypeDto;
/** 字典项 DTO 的唯一来源是审核后的生成契约。 */
export type DictionaryItem = SwiftExportContractsDTOsSystemDtosSysDictDataDto;

/** 字典项集合拥有的完整可写投影；未展示的编码与样式仍随编辑会话保留。 */
export interface DictionaryItemValues {
  dictCode: string;
  dataNameEn: string;
  dataNameZh: string;
  remark: string | null;
  sortOrder: number | null;
  cssClass: string | null;
  isEnabled: boolean;
}

/** 原 DTO 只作只读基线快照，当前字段仍唯一属于 shared 编辑集合。 */
export type DictionaryItemRow = EditableRow<DictionaryItemValues> & {
  readonly source: DictionaryItem;
};

const protectedCodes = new Set(
  [
    "CustomerType",
    "Incoterm",
    "PaymentMethod",
    "ShippedBy",
    "Unit",
    "FlowTestFunction",
    "FlowLimiterSpecification",
  ].map((code) => code.toLowerCase()),
);

/** 对齐当前后端系统字典保护；不能因历史 IsSystem 标记错误而允许删改系统编码。 */
export function isProtectedDictionaryType(type: DictionaryType): boolean {
  return type.isSystem === true || isSystemDictionaryCode(type.dictCode);
}

/** 旧品质字典仅供迁移追溯；沿用现有 WPF 维护入口的排除范围。 */
export function isVisibleDictionaryType(type: DictionaryType): boolean {
  return type.dictCode?.trimEnd().toLowerCase() !== "aftersalesqualitycategory";
}

/** 当前后端稳定编码集的精确匹配，只用于字典业务保护，不推测权限。 */
export function isSystemDictionaryCode(
  code: string | null | undefined,
): boolean {
  return protectedCodes.has(code?.trim().toLowerCase() ?? "");
}

/** 从服务器快照提取全部可写字段；Uid、Version、IsSystem 和审计字段不参与编辑。 */
export function projectDictionaryItemValues(
  item: DictionaryItem,
): DictionaryItemValues {
  return {
    dictCode: item.dictCode ?? "",
    dataNameEn: item.dataNameEn ?? "",
    dataNameZh: item.dataNameZh ?? "",
    remark: item.remark ?? null,
    sortOrder: item.sortOrder ?? null,
    cssClass: item.cssClass ?? null,
    isEnabled: item.isEnabled === true,
  };
}

/** 把读取快照变成独立编辑基线；拒绝无身份、无版本或重复身份的残缺响应。 */
export function createDictionaryItemRows(
  items: readonly DictionaryItem[],
): DictionaryItemRow[] {
  const seen = new Set<string>();
  return items.map((item) => {
    if (
      !isDictionaryUid(item.uid) ||
      !isDictionaryVersion(item.version) ||
      seen.has(item.uid.toLowerCase())
    )
      throw new Error("字典项快照缺少唯一身份或有效基线版本。");
    seen.add(item.uid.toLowerCase());
    return {
      ...createEditableRow(
        item.uid,
        item.uid,
        item.version,
        projectDictionaryItemValues(item),
      ),
      source: structuredClone(item),
    };
  });
}

/** 新增行仅有本地 ClientKey 和当前投影，服务器 Uid/Version 由成功回执给出。 */
export function createDictionaryItemRow(
  dictCode: string,
  clientKey: string = crypto.randomUUID(),
): DictionaryItemRow {
  const source: DictionaryItem = { dictCode, isSystem: false, isEnabled: true };
  return {
    ...createCreatedEditableRow(clientKey, projectDictionaryItemValues(source)),
    source,
  };
}

/** 复用公共修改算法并保留额外的只读 DTO 快照，不另造增改删状态机。 */
export function updateDictionaryItemRows(
  rows: readonly DictionaryItemRow[],
  clientKey: string,
  patch: Partial<DictionaryItemValues>,
): DictionaryItemRow[] {
  return preserveSources(updateEditableRow(rows, clientKey, patch), rows);
}

/** 新增行移除、既有行标删仍由公共集合处理，系统项保护由操作入口核对。 */
export function removeDictionaryItemRows(
  rows: readonly DictionaryItemRow[],
  clientKey: string,
): DictionaryItemRow[] {
  const target = rows.find((row) => row.clientKey === clientKey);
  if (target?.origin !== "created" && target?.source.isSystem === true)
    throw new Error("系统字典项不能删除。");
  return preserveSources(removeEditableRow(rows, clientKey), rows);
}

/** 恢复标删行时保留删除前字段与原 DTO，供同一会话继续编辑。 */
export function restoreDictionaryItemRows(
  rows: readonly DictionaryItemRow[],
  clientKey: string,
): DictionaryItemRow[] {
  return preserveSources(restoreEditableRow(rows, clientKey), rows);
}

/** 仅在调用方完整关闭保护允许时放弃集合，清新增并恢复既有基线。 */
export function discardDictionaryItemRows(
  rows: readonly DictionaryItemRow[],
): DictionaryItemRow[] {
  return preserveSources(discardEditableRows(rows), rows);
}

/** 原始排序输入只接受 Int32 或明确允许的空值，不把无效/未完成文本变成零。 */
export function parseDictionarySortOrder(
  raw: string,
  nullable = true,
): ParsedInput<number | null> {
  const parsed = parseIntegerText(raw, nullable);
  return parsed.valid && (parsed.value === null || isInt32(parsed.value))
    ? parsed
    : { valid: false };
}

/** 对齐后端必填字段与 Int32 类型；不猜测数据库唯一性或服务器事务结果。 */
export function validateDictionaryItemValues(
  values: DictionaryItemValues,
): string | null {
  if (!values.dictCode.trim()) return "字典编码不能为空。";
  if (!values.dataNameEn.trim()) return "英文名称不能为空。";
  if (!values.dataNameZh.trim()) return "中文名称不能为空。";
  if (values.sortOrder !== null && !isInt32(values.sortOrder))
    return "排序必须是有效整数或空值。";
  return null;
}

/** 类型编辑只校验当前 DTO 字段；缺失父节点、重名和循环最终由真实后端核实。 */
export function validateDictionaryType(type: DictionaryType): string | null {
  if (!type.dictCode?.trim()) return "字典编码不能为空。";
  if (!type.dictName?.trim()) return "字典名称不能为空。";
  if (!isInt32(type.sortOrder ?? 0)) return "排序必须是有效整数。";
  if (type.parentId != null && !isDictionaryUid(type.parentId))
    return "上级类型身份无效。";
  if (
    type.parentId &&
    type.uid &&
    type.parentId.toLowerCase() === type.uid.toLowerCase()
  )
    return "字典类型不能引用自身作为上级。";
  return null;
}

/** 非空 Guid 是实体身份的最低契约；ClientKey 不采用这个判断。 */
export function isDictionaryUid(value: unknown): value is string {
  return (
    typeof value === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      value,
    ) &&
    value.toLowerCase() !== "00000000-0000-0000-0000-000000000000"
  );
}

/** Version 为服务端正 Int32 并发凭据，前端不从显示状态推算。 */
export function isDictionaryVersion(value: unknown): value is number {
  return typeof value === "number" && isInt32(value) && value > 0;
}

/** 系统 DTO 使用 Int32，不能仅以 JavaScript safe integer 作为合法传输条件。 */
function isInt32(value: number): boolean {
  return (
    Number.isInteger(value) && value >= -2_147_483_648 && value <= 2_147_483_647
  );
}

/** 公共算法保留行身份；仅恢复它的额外只读来源类型，不复制业务当前值。 */
function preserveSources(
  nextRows: readonly EditableRow<DictionaryItemValues>[],
  originalRows: readonly DictionaryItemRow[],
): DictionaryItemRow[] {
  const sources = new Map(
    originalRows.map((row) => [row.clientKey, row.source]),
  );
  return nextRows.map((row) => {
    const source = sources.get(row.clientKey);
    if (!source) throw new Error("字典编辑行缺少原 DTO 快照。");
    return { ...row, source };
  });
}
