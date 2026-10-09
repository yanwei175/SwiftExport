import { describe, expect, it } from "vitest";
import { preserveSelectedOption, preserveSelectedOptions } from "./候选选择";

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
  it("多选缺项保留已选标签并去重，不改变当前数组或候选读取结果", () => {
    const values = ["a", "a", "b"] as const;
    const options = [{ value: "b", label: "B" }];
    expect(
      preserveSelectedOptions(options, values, [
        { value: "a", label: "已有A" },
      ]),
    ).toEqual([
      { value: "a", label: "已有A", disabled: true },
      { value: "b", label: "B" },
    ]);
    expect(values).toEqual(["a", "a", "b"]);
    expect(options).toEqual([{ value: "b", label: "B" }]);
    expect(preserveSelectedOptions([], [0, 2])).toEqual([
      { value: 0, label: "0", disabled: true },
      { value: 2, label: "2", disabled: true },
    ]);
    expect(preserveSelectedOptions([], [])).toEqual([]);
  });
});
