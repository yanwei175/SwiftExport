import { describe, expect, it } from "vitest";
import { locateFieldIssues } from "./字段错误";

describe("字段错误经明确映射定位", () => {
  it("聚合同字段错误，保留未知字段且单元格按ClientKey定位", () => {
    const result = locateFieldIssues(
      [
        { field: "Header.Name", message: "名称不能为空" },
        { field: "Header.Name", message: "名称重复" },
        { field: "LineA.Quantity", message: "数量超界" },
        { field: "Unknown", message: "未知字段仍可见" },
        { message: "整页业务说明" },
      ],
      (field) =>
        field === "Header.Name"
          ? { kind: "form", name: ["name"] }
          : field === "LineA.Quantity"
            ? { kind: "cell", clientKey: "stable-a", columnKey: "quantity" }
            : undefined,
    );
    expect(result.form).toEqual([
      { name: ["name"], errors: ["名称不能为空", "名称重复"] },
    ]);
    expect(result.cells).toEqual([
      { clientKey: "stable-a", columnKey: "quantity", errors: ["数量超界"] },
    ]);
    expect(result.page).toEqual(["未知字段仍可见", "整页业务说明"]);
  });
});
