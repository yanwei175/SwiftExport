import type { VerificationValue } from "./模拟保存";
import { hasInputChanges } from "../../../shared/editing/未提交输入";
import type { UncommittedInput } from "../../../shared/editing/未提交输入";

/** 缓冲仅保存当前控件的原始内容及进入编辑时值，不形成第二份长期业务数据。 */
export type CellBuffer = UncommittedInput<
  keyof VerificationValue,
  string | number
>;

/** 数量必须为正整数；空串、小数及未完成负号保持无效原始输入。 */
export function parseQuantity(raw: string): number | null {
  if (!/^\d+$/.test(raw.trim())) return null;
  const value = Number(raw);
  return Number.isSafeInteger(value) && value > 0 ? value : null;
}

/** 按字段业务等价规则判断实际输入变化，不把聚焦或曾经输入当永久修改。 */
export function bufferNeedsProtection(buffer: CellBuffer): boolean {
  return hasInputChanges(buffer, (raw) => {
    if (buffer.field === "name") return { valid: true, value: raw };
    const value = parseQuantity(raw);
    return value === null ? { valid: false } : { valid: true, value };
  });
}
