import { describe, expect, it } from "vitest";
import {
  buildGridFillDownIntent,
  constrainGridColumnWidth,
  gridLayoutSignature,
  moveGridCell,
  resolveGridCellRange,
} from "./单元格区域";
import type { GridCellSelection } from "./单元格区域";

const rowKeys = ["r1", "r2", "r3", "r4"];
const columnKeys = ["name", "quantity", "version"];
const layout = gridLayoutSignature(rowKeys, columnKeys);

/** 纯算法证据检验稳定身份与矩形边界，不替代鼠标、输入框和 Activity 的浏览器验收。 */
describe("单元格区域", () => {
  it("反向扩区归一化，顶部可见行作为每列的填充源", () => {
    const selection: GridCellSelection = {
      anchor: { rowKey: "r4", columnKey: "quantity" },
      end: { rowKey: "r2", columnKey: "name" },
      layout,
    };
    const range = resolveGridCellRange(selection, rowKeys, columnKeys, layout);
    expect(range).toEqual({
      firstRow: 1,
      lastRow: 3,
      firstColumn: 0,
      lastColumn: 1,
    });
    const rows = rowKeys.map((key) => ({ key }));
    expect(
      buildGridFillDownIntent(
        range,
        rows,
        columnKeys,
        () => true,
        () => true,
      ),
    ).toEqual({
      columns: [
        {
          columnKey: "name",
          fields: ["name"],
          source: rows[1],
          targets: [rows[2], rows[3]],
        },
        {
          columnKey: "quantity",
          fields: ["quantity"],
          source: rows[1],
          targets: [rows[2], rows[3]],
        },
      ],
    });
  });

  it("只读列和目标被跳过，不包含区域外行列", () => {
    const rows = rowKeys.map((key) => ({ key }));
    const range = { firstRow: 0, lastRow: 2, firstColumn: 1, lastColumn: 2 };
    expect(
      buildGridFillDownIntent(
        range,
        rows,
        columnKeys,
        (key) => key !== "version",
        (row) => row.key !== "r2",
      ),
    ).toEqual({
      columns: [
        {
          columnKey: "quantity",
          fields: ["quantity"],
          source: rows[0],
          targets: [rows[2]],
        },
      ],
    });
    expect(
      buildGridFillDownIntent(
        range,
        rows,
        columnKeys,
        () => true,
        (row) => row.key !== "r1",
      ),
    ).toBeNull();
  });

  it("一列声明多个业务字段，未展示字段同样传递且重复标识只保留一次", () => {
    const fields = ["relatedId", "displayName", "relatedId"];
    const rows = [
      { relatedId: "id-a", displayName: "源值" },
      { relatedId: "id-b", displayName: "目标值" },
    ];
    const intent = buildGridFillDownIntent(
      { firstRow: 0, lastRow: 1, firstColumn: 0, lastColumn: 0 },
      rows,
      ["lookup"],
      () => true,
      () => true,
      () => fields,
    );
    expect(intent).toEqual({
      columns: [
        {
          columnKey: "lookup",
          fields: ["relatedId", "displayName"],
          source: rows[0],
          targets: [rows[1]],
        },
      ],
    });
    expect(intent?.columns[0]?.fields).not.toBe(fields);
    expect(intent?.columns[0]?.source).toBe(rows[0]);
    expect(rows[1]).toEqual({ relatedId: "id-b", displayName: "目标值" });
    expect(fields).toEqual(["relatedId", "displayName", "relatedId"]);
  });

  it("未声明映射时只传列 key，空映射禁用该列而不替换成默认映射", () => {
    const rows = [{ key: "r1" }, { key: "r2" }];
    const range = { firstRow: 0, lastRow: 1, firstColumn: 0, lastColumn: 1 };
    const defaults = buildGridFillDownIntent(
      range,
      rows,
      columnKeys,
      () => true,
      () => true,
    );
    expect(defaults?.columns.map(({ fields }) => fields)).toEqual([
      ["name"],
      ["quantity"],
    ]);
    const explicit = buildGridFillDownIntent(
      range,
      rows,
      columnKeys,
      () => true,
      () => true,
      (key) => (key === "name" ? [] : ["amount"]),
    );
    expect(
      explicit?.columns.map(({ columnKey, fields }) => ({ columnKey, fields })),
    ).toEqual([{ columnKey: "quantity", fields: ["amount"] }]);
    expect(
      buildGridFillDownIntent(
        range,
        rows,
        columnKeys,
        () => true,
        () => true,
        () => [],
      ),
    ).toBeNull();
  });

  it("任何可填列声明空白字段时拒绝整个区域，不修正字段名或只发出其他列", () => {
    const rows = [{ key: "r1" }, { key: "r2" }];
    const range = { firstRow: 0, lastRow: 1, firstColumn: 0, lastColumn: 1 };
    for (const invalid of [
      "",
      " ",
      " name",
      "name ",
      "display name",
      "name\n",
    ]) {
      expect(
        buildGridFillDownIntent(
          range,
          rows,
          columnKeys,
          () => true,
          () => true,
          (key) => (key === "quantity" ? ["amount", invalid] : [key]),
        ),
      ).toBeNull();
    }
  });

  it("不可填列或只读源不读取映射，不解析业务字段路径", () => {
    const rows = [{ key: "r1" }, { key: "r2" }];
    const range = { firstRow: 0, lastRow: 1, firstColumn: 0, lastColumn: 2 };
    const queried: string[] = [];
    const intent = buildGridFillDownIntent(
      range,
      rows,
      columnKeys,
      (key) => key !== "version",
      (_row, key) => key !== "quantity",
      (key) => {
        queried.push(key);
        return ["business.value"];
      },
    );
    expect(queried).toEqual(["name"]);
    expect(intent?.columns[0]?.fields).toEqual(["business.value"]);
    expect(rows).toEqual([{ key: "r1" }, { key: "r2" }]);
  });

  it("单选及单行区域不产生填充", () => {
    const rows = [{ key: "r1" }];
    expect(
      buildGridFillDownIntent(
        { firstRow: 0, lastRow: 0, firstColumn: 0, lastColumn: 2 },
        rows,
        columnKeys,
        () => true,
        () => true,
      ),
    ).toBeNull();
    expect(
      buildGridFillDownIntent(
        null,
        rows,
        columnKeys,
        () => true,
        () => true,
      ),
    ).toBeNull();
  });

  it("分页、排序和隐藏列后旧范围立即无效", () => {
    const selection: GridCellSelection = {
      anchor: { rowKey: "r1", columnKey: "name" },
      end: { rowKey: "r3", columnKey: "quantity" },
      layout,
    };
    for (const next of [
      gridLayoutSignature([...rowKeys].reverse(), columnKeys),
      gridLayoutSignature(rowKeys, ["name", "version"]),
      gridLayoutSignature(rowKeys, columnKeys, 2),
    ])
      expect(
        resolveGridCellRange(selection, rowKeys, columnKeys, next),
      ).toBeNull();
  });

  it("字段值变化不改变身份签名，缺失锚点不恢复成其他行", () => {
    expect(gridLayoutSignature([...rowKeys], [...columnKeys])).toBe(layout);
    expect(
      resolveGridCellRange(
        {
          anchor: { rowKey: "gone", columnKey: "name" },
          end: { rowKey: "r1", columnKey: "name" },
          layout,
        },
        rowKeys,
        columnKeys,
        layout,
      ),
    ).toBeNull();
    expect(gridLayoutSignature([1], ["name"])).not.toBe(
      gridLayoutSignature(["1"], ["name"]),
    );
  });

  it("键盘移动逐格且在边缘钳制，失效身份不能移动", () => {
    expect(
      moveGridCell(
        { rowKey: "r1", columnKey: "name" },
        rowKeys,
        columnKeys,
        -1,
        -1,
      ),
    ).toEqual({ rowKey: "r1", columnKey: "name" });
    expect(
      moveGridCell(
        { rowKey: "r1", columnKey: "name" },
        rowKeys,
        columnKeys,
        1,
        1,
      ),
    ).toEqual({ rowKey: "r2", columnKey: "quantity" });
    expect(
      moveGridCell(
        { rowKey: "gone", columnKey: "name" },
        rowKeys,
        columnKeys,
        1,
        0,
      ),
    ).toBeNull();
  });

  it("列宽限制为有效像素，不接受无穷或 NaN", () => {
    expect(
      [0, 48, 180.6, 2000, Infinity, NaN].map(constrainGridColumnWidth),
    ).toEqual([48, 48, 181, 1000, 160, 160]);
  });
});
