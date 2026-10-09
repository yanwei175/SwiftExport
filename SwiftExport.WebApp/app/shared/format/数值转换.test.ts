import { describe, expect, it } from "vitest";
import {
  decimalTextToJsonNumber,
  parseDecimalText,
  parseIntegerText,
} from "./数值转换";

describe("数值输入只按明确字段契约转换", () => {
  it("零与空值分离，未完成输入及不安全整数不被强制转换", () => {
    expect(parseIntegerText("0")).toEqual({ valid: true, value: 0 });
    expect(parseIntegerText(" ")).toEqual({ valid: false });
    expect(parseIntegerText(" ", true)).toEqual({ valid: true, value: null });
    for (const text of ["-", "1e", "1.1", "9007199254740993"])
      expect(parseIntegerText(text).valid).toBe(false);
    expect(parseIntegerText("+001")).toEqual({ valid: true, value: 1 });
  });
  it("十进制文本保留精度，JSON number 映射不允许静默舍入", () => {
    expect(parseDecimalText("0012.500")).toEqual({
      valid: true,
      value: "12.5",
    });
    expect(parseDecimalText("-0.00")).toEqual({ valid: true, value: "0" });
    expect(parseDecimalText("", true)).toEqual({ valid: true, value: null });
    expect(parseDecimalText("1.0000000000000001")).toEqual({
      valid: true,
      value: "1.0000000000000001",
    });
    for (const text of ["-", ".", "1.", "1e2", "Infinity", "NaN"])
      expect(parseDecimalText(text).valid).toBe(false);
    expect(decimalTextToJsonNumber("0.10")).toEqual({
      valid: true,
      value: 0.1,
    });
    expect(decimalTextToJsonNumber("0.0000001")).toEqual({
      valid: true,
      value: 0.0000001,
    });
    expect(decimalTextToJsonNumber("9007199254740993").valid).toBe(false);
    expect(decimalTextToJsonNumber("1.0000000000000001").valid).toBe(false);
  });
});
