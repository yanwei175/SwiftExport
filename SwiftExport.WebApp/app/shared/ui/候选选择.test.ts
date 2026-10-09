import { describe, expect, it } from "vitest";
import { preserveSelectedOption } from "./候选选择";

describe("候选搜索与业务选择值分离", () => {
  it("结果缺项保留当前标签，但显示补项不产生额外合法可选值", () => {
    expect(
      preserveSelectedOption([{ value: "b", label: "B" }], "a", {
        value: "a",
        label: "已有A",
      }),
    ).toEqual([
      { value: "a", label: "已有A", disabled: true },
      { value: "b", label: "B" },
    ]);
    expect(preserveSelectedOption([], null)).toEqual([]);
    expect(preserveSelectedOption([{ value: 0, label: "零" }], 0)).toEqual([
      { value: 0, label: "零" },
    ]);
  });
});
