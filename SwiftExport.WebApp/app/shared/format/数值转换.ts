import type { ParsedInput } from "../editing/未提交输入";

/** 仅解析明确的十进制整数；空值是否允许由字段调用方显式决定。 */
export function parseIntegerText(
  raw: string,
  nullable = false,
): ParsedInput<number | null> {
  const text = raw.trim();
  if (!text) return nullable ? { valid: true, value: null } : { valid: false };
  if (!/^[+-]?\d+$/.test(text)) return { valid: false };
  const value = Number(text);
  return Number.isSafeInteger(value)
    ? { valid: true, value: value === 0 ? 0 : value }
    : { valid: false };
}

/** 保留精确十进制文本；不把未完成输入、空串或指数文本当作合法金额。 */
export function parseDecimalText(
  raw: string,
  nullable = false,
): ParsedInput<string | null> {
  const text = raw.trim();
  if (!text) return nullable ? { valid: true, value: null } : { valid: false };
  if (!/^[+-]?(?:\d+(?:\.\d+)?|\.\d+)$/.test(text)) return { valid: false };
  const negative = text.startsWith("-");
  const [whole = "", fraction = ""] = text.replace(/^[+-]/, "").split(".");
  const integer = whole.replace(/^0+/, "") || "0";
  const decimal = fraction.replace(/0+$/, "");
  const sign = negative && (integer !== "0" || decimal !== "") ? "-" : "";
  return {
    valid: true,
    value: `${sign}${integer}${decimal ? `.${decimal}` : ""}`,
  };
}

/** 仅供既有 JSON number 字段映射；往返精度不一致时拒绝，不进行财务计算。 */
export function decimalTextToJsonNumber(raw: string): ParsedInput<number> {
  const parsed = parseDecimalText(raw);
  if (!parsed.valid || parsed.value === null) return { valid: false };
  const value = Number(parsed.value);
  if (
    !Number.isFinite(value) ||
    (Number.isInteger(value) && !Number.isSafeInteger(value))
  )
    return { valid: false };
  const roundTrip = parseDecimalText(expandExponent(String(value)));
  return roundTrip.valid && roundTrip.value === parsed.value
    ? { valid: true, value }
    : { valid: false };
}

/** 将 JavaScript 的指数显示展开用于文本对账，不改变数值或后端传输约定。 */
function expandExponent(value: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?e([+-]?\d+)$/i.exec(value);
  if (!match) return value;
  const [, sign = "", integer = "", fraction = "", exponent = "0"] = match;
  const digits = integer + fraction;
  const position = integer.length + Number(exponent);
  if (position <= 0) return `${sign}0.${"0".repeat(-position)}${digits}`;
  if (position >= digits.length)
    return `${sign}${digits}${"0".repeat(position - digits.length)}`;
  return `${sign}${digits.slice(0, position)}.${digits.slice(position)}`;
}
