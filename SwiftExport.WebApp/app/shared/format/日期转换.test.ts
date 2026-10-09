import { describe, expect, it } from "vitest";
import { parseBusinessDate, parseInstant } from "./日期转换";

describe("日历日期与带时区时刻分开处理", () => {
  it("严格校验真实日历，业务日期始终保持原日不转UTC", () => {
    expect(parseBusinessDate("2024-02-29")).toEqual({
      valid: true,
      value: "2024-02-29",
    });
    expect(parseBusinessDate("0070-01-01")).toEqual({
      valid: true,
      value: "0070-01-01",
    });
    expect(parseBusinessDate(null, true)).toEqual({ valid: true, value: null });
    for (const text of [
      "2023-02-29",
      "2024-04-31",
      "2024-13-01",
      "0000-01-01",
      "2024-1-1",
      "2024-01-01T00:00:00Z",
    ])
      expect(parseBusinessDate(text).valid).toBe(false);
  });
  it("时刻要求显式时区并保留亚毫秒精度，不接受本地时间猜测", () => {
    const value = "2026-10-09T12:34:56.1234567+08:00";
    expect(parseInstant(value)).toEqual({ valid: true, value });
    expect(parseInstant("2026-10-09T00:00:00Z").valid).toBe(true);
    for (const text of [
      "2026-10-09T00:00:00",
      "2026-10-09",
      "2026-02-30T00:00:00Z",
      "2026-10-09T24:00:00Z",
      "2026-10-09T00:00:60Z",
      "2026-10-09T00:00:00+14:01",
    ])
      expect(parseInstant(text).valid).toBe(false);
  });
});
