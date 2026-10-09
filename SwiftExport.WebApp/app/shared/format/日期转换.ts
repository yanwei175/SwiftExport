import type { ParsedInput } from "../editing/未提交输入";

/** 业务日期只返回 YYYY-MM-DD，不转 UTC 时刻；严格检查闰年及真实日历范围。 */
export function parseBusinessDate(
  raw: string | null,
  nullable = false,
): ParsedInput<string | null> {
  const text = raw?.trim() ?? "";
  if (!text) return nullable ? { valid: true, value: null } : { valid: false };
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
  if (!match) return { valid: false };
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31)
    return { valid: false };
  const date = new Date(`${text}T00:00:00.000Z`);
  return date.getUTCFullYear() === year &&
    date.getUTCMonth() + 1 === month &&
    date.getUTCDate() === day
    ? { valid: true, value: text }
    : { valid: false };
}

/** 时刻必须显式带时区，保留原始小数秒精度；不将无时区字符串猜成浏览器本地时间。 */
export function parseInstant(
  raw: string | null,
  nullable = false,
): ParsedInput<string | null> {
  const text = raw?.trim() ?? "";
  if (!text) return nullable ? { valid: true, value: null } : { valid: false };
  const match =
    /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(
      text,
    );
  if (!match || !parseBusinessDate(match[1] ?? "").valid)
    return { valid: false };
  if (Number(match[2]) > 23 || Number(match[3]) > 59 || Number(match[4]) > 59)
    return { valid: false };
  const offset = match[6] ?? "";
  if (offset !== "Z") {
    const hour = Number(offset.slice(1, 3));
    const minute = Number(offset.slice(4));
    if (hour > 14 || minute > 59 || (hour === 14 && minute !== 0))
      return { valid: false };
  }
  return Number.isFinite(Date.parse(text))
    ? { valid: true, value: text }
    : { valid: false };
}
