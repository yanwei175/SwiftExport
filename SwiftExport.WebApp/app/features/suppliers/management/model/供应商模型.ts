import type { SwiftExportContractsDTOsSuppliersSupplierDto } from "../../../../shared/api/generated/client";
import {
  createCreatedEditableRow,
  createEditableRow,
} from "../../../../shared/editing/编辑集合";
import type { EditableRow } from "../../../../shared/editing/编辑集合";
import type { ParsedInput } from "../../../../shared/editing/未提交输入";
import {
  decimalTextToJsonNumber,
  parseIntegerText,
} from "../../../../shared/format/数值转换";

/** 供应商传输类型仅引用审核后的 Orval 契约。 */
export type SupplierDto = SwiftExportContractsDTOsSuppliersSupplierDto;

/** 编辑集合拥有唯一的供应商可写字段，身份与审计信息保留在只读来源中。 */
export interface SupplierValues {
  supplierCode: string;
  supplierName: string;
  docRelateNo: string;
  contactPerson: string;
  contactEmail: string;
  contactPhone: string;
  address: string;
  currency: string;
  paymentTerms: string;
  zheKouBeiZhu: string;
  zheKou: number | null;
  taxRate: number | null;
  defaultLeadTime: number | null;
}

/** 字段定位与原始输入转换使用同一可写字段目录。 */
export type SupplierField = keyof SupplierValues;
/** 单元格转换结果只含供应商已有契约值，不引入额外启停字段。 */
export type SupplierValue = SupplierValues[SupplierField];
/** 来源 DTO 是读取快照，不作为第二份可写当前值。 */
export interface SupplierRow extends EditableRow<SupplierValues> {
  readonly source: SupplierDto;
}

/** 中文字段目录用于表格列、字段错误定位及恢复材料说明。 */
export const supplierFieldTitles: Record<SupplierField, string> = {
  supplierCode: "供应商代码",
  supplierName: "供应商名称",
  docRelateNo: "单据关联号",
  contactPerson: "联系人",
  contactEmail: "邮箱",
  contactPhone: "电话",
  address: "地址",
  currency: "币种",
  paymentTerms: "付款条款",
  zheKouBeiZhu: "折扣备注",
  zheKou: "折扣",
  taxRate: "税率",
  defaultLeadTime: "默认交期",
};

/** 三个必填文本沿用后端 Trim，其余文本保留原值，空数字保持 null。 */
export function projectSupplierValues(dto: SupplierDto): SupplierValues {
  return {
    supplierCode: dto.supplierCode?.trim() ?? "",
    supplierName: dto.supplierName?.trim() ?? "",
    docRelateNo: dto.docRelateNo?.trim() ?? "",
    contactPerson: dto.contactPerson ?? "",
    contactEmail: dto.contactEmail ?? "",
    contactPhone: dto.contactPhone ?? "",
    address: dto.address ?? "",
    currency: dto.currency ?? "",
    paymentTerms: dto.paymentTerms ?? "",
    zheKouBeiZhu: dto.zheKouBeiZhu ?? "",
    zheKou: dto.zheKou ?? null,
    taxRate: dto.taxRate ?? null,
    defaultLeadTime: dto.defaultLeadTime ?? null,
  };
}

/** 每个读取行形成独立基线，缺失或重复服务器身份不能进入编辑集合。 */
export function createSupplierRows(
  items: readonly SupplierDto[],
): SupplierRow[] {
  const seen = new Set<string>();
  return items.map((dto) => {
    if (
      !isSupplierUid(dto.uid) ||
      !isSupplierVersion(dto.version) ||
      seen.has(dto.uid.toLowerCase())
    )
      throw new Error("供应商快照缺少唯一身份或有效基线版本。");
    seen.add(dto.uid.toLowerCase());
    return {
      ...createEditableRow(
        dto.uid,
        dto.uid,
        dto.version,
        projectSupplierValues(dto),
      ),
      source: structuredClone(dto),
    };
  });
}

/** 新行仅有本地 ClientKey；CNY 沿用 WPF 默认，单据关联号等待用户填写。 */
export function createSupplierRow(
  clientKey: string = crypto.randomUUID(),
): SupplierRow {
  const source: SupplierDto = { currency: "CNY" };
  return {
    ...createCreatedEditableRow(clientKey, projectSupplierValues(source)),
    source,
  };
}

/** 复用精度与整数转换，未完成文本、越界值或无法精确往返的小数不会写入集合。 */
export function parseSupplierInput(
  field: SupplierField,
  raw: string,
): ParsedInput<SupplierValue> {
  if (field === "zheKou" || field === "taxRate") {
    if (!raw.trim()) return { valid: true, value: null };
    const parsed = decimalTextToJsonNumber(raw);
    return parsed.valid && parsed.value >= 0 && parsed.value <= 1
      ? parsed
      : { valid: false };
  }
  if (field === "defaultLeadTime") {
    const parsed = parseIntegerText(raw, true);
    return parsed.valid &&
      (parsed.value === null || isNonnegativeInt32(parsed.value))
      ? parsed
      : { valid: false };
  }
  return {
    valid: true,
    value: ["supplierCode", "supplierName", "docRelateNo"].includes(field)
      ? raw.trim()
      : raw,
  };
}

/** 对齐供应商必填、比例与交期边界；重名和关联删除最终由服务器核实。 */
export function validateSupplierValues(
  values: SupplierValues,
): Partial<Record<SupplierField, string>> {
  const errors: Partial<Record<SupplierField, string>> = {};
  for (const field of ["supplierCode", "supplierName", "docRelateNo"] as const)
    if (!values[field].trim())
      errors[field] = `${supplierFieldTitles[field]}不能为空。`;
  for (const field of ["zheKou", "taxRate"] as const) {
    const value = values[field];
    if (value !== null && (!Number.isFinite(value) || value < 0 || value > 1))
      errors[field] = `${supplierFieldTitles[field]}必须在 0 到 1 之间。`;
  }
  if (
    values.defaultLeadTime !== null &&
    !isNonnegativeInt32(values.defaultLeadTime)
  )
    errors.defaultLeadTime = "默认交期必须是非负整数或空值。";
  return errors;
}

/** 非空 Guid 表示服务器实体身份；本地 ClientKey 不受 Guid 格式约束。 */
export function isSupplierUid(value: unknown): value is string {
  return (
    typeof value === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      value,
    ) &&
    value.toLowerCase() !== "00000000-0000-0000-0000-000000000000"
  );
}

/** 服务端 Version 是正 Int32，客户端不从行状态补造并发版本。 */
export function isSupplierVersion(value: unknown): value is number {
  return typeof value === "number" && isNonnegativeInt32(value) && value > 0;
}

/** 交期使用 C# Int32 边界，不能只用 JavaScript 安全整数范围。 */
function isNonnegativeInt32(value: number): boolean {
  return Number.isInteger(value) && value >= 0 && value <= 2_147_483_647;
}
