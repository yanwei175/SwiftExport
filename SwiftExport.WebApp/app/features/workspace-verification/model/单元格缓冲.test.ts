import { describe, expect, it } from "vitest";
import { bufferNeedsProtection, parseQuantity } from "./单元格缓冲";

describe("尚未进入编辑集合的数量输入", () => {
  it("无效及未完成输入受保护，恢复业务原值解除保护", () => {
    for (const raw of ["", "-", "1.5", "abc", "0"]) {
      expect(parseQuantity(raw)).toBeNull();
      expect(
        bufferNeedsProtection({
          clientKey: "a",
          field: "quantity",
          raw,
          original: 1,
        }),
      ).toBe(true);
    }
    expect(
      bufferNeedsProtection({
        clientKey: "a",
        field: "quantity",
        raw: "2",
        original: 1,
      }),
    ).toBe(true);
    expect(
      bufferNeedsProtection({
        clientKey: "a",
        field: "quantity",
        raw: "01",
        original: 1,
      }),
    ).toBe(false);
    expect(
      bufferNeedsProtection({
        clientKey: "a",
        field: "quantity",
        raw: "1",
        original: 1,
      }),
    ).toBe(false);
  });
});
