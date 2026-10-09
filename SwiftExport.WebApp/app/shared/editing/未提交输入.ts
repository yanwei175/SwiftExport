/** 控件短期缓冲只保存原始输入和进入编辑时值；业务当前值仍属于 Form 或编辑集合。 */
export interface UncommittedInput<Field extends string, Value> {
  clientKey: string;
  field: Field;
  raw: string;
  original: Value;
}

/** 转换结果显式区分合法业务值和未完成输入，不把空串强制转换为零。 */
export type ParsedInput<Value> =
  { valid: true; value: Value } | { valid: false };

/** 使用 feature 的字段转换/相等策略判断保护；聚焦或 touched 不参与判断。 */
export function hasInputChanges<Field extends string, Value>(
  input: UncommittedInput<Field, Value>,
  parse: (raw: string) => ParsedInput<Value>,
  equals: (left: Value, right: Value) => boolean = Object.is,
): boolean {
  const parsed = parse(input.raw);
  return !parsed.valid || !equals(parsed.value, input.original);
}
