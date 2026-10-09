import type { VerificationValue } from "./模拟保存";

/** 缓冲仅保存当前控件的原始内容及进入编辑时值，不形成第二份长期业务数据。 */
export interface CellBuffer {
  clientKey: string;
  field: keyof VerificationValue;
  raw: string;
  original: string | number;
}

/** 数量必须为正整数；空串、小数及未完成负号保持无效原始输入。 */
export function parseQuantity(raw: string): number | null {
  if (!/^\d+$/.test(raw.trim())) return null;
  const value = Number(raw);
  return Number.isSafeInteger(value) && value > 0 ? value : null;
}

/** 按字段业务等价规则判断实际输入变化，不把聚焦或曾经输入当永久修改。 */
export function bufferNeedsProtection(buffer: CellBuffer): boolean {
  if (buffer.field === "name") return buffer.raw !== buffer.original;
  const value = parseQuantity(buffer.raw);
  return value === null || value !== buffer.original;
}
