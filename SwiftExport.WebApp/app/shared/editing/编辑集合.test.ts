import { describe, expect, it } from "vitest";
import {
  acceptDeletedEditableRow,
  acceptEditableRow,
  addEditableRow,
  createCreatedEditableRow,
  createEditableRow,
  discardEditableRows,
  getEditableChanges,
  getVisibleEditableRows,
  isRowDirty,
  removeEditableRow,
  restoreEditableRow,
  updateEditableRow,
} from "./编辑集合";

describe("编辑集合的基线与当前值", () => {
  it("修改再还原恢复 clean，基线不会被当前值污染", () => {
    const row = createEditableRow("a", "uid-a", 1, {
      name: "原值",
      quantity: 1,
    });
    const changed = updateEditableRow([row], "a", { quantity: 2 });
    expect(changed[0]?.baseline.quantity).toBe(1);
    expect(changed[0] && isRowDirty(changed[0])).toBe(true);
    const restored = updateEditableRow(changed, "a", { quantity: 1 });
    expect(restored[0] && isRowDirty(restored[0])).toBe(false);
    expect(row.current.quantity).toBe(1);
  });

  it("部分接纳只提高确认成功行版本，失败行修改仍然保留", () => {
    const rows = [
      createEditableRow("a", "uid-a", 1, { name: "A", quantity: 1 }),
      createEditableRow("b", "uid-b", 1, { name: "B", quantity: 1 }),
    ];
    const changed = updateEditableRow(
      updateEditableRow(rows, "a", { quantity: 2 }),
      "b",
      { quantity: 3 },
    );
    const accepted = acceptEditableRow(changed, "a", "uid-a", 2, {
      name: "A",
      quantity: 2,
    });
    expect(accepted[0]?.version).toBe(2);
    expect(accepted[0] && isRowDirty(accepted[0])).toBe(false);
    expect(accepted[1]?.version).toBe(1);
    expect(accepted[1] && isRowDirty(accepted[1])).toBe(true);
    expect(discardEditableRows(accepted)[1]?.current.quantity).toBe(1);
  });

  it("首次新增立即产生 Create，新增后删除不产生服务器 Delete", () => {
    const existing = createEditableRow("existing", "uid-existing", 4, {
      name: "既有行",
    });
    const created = createCreatedEditableRow("new", { name: "新行" });
    const rows = addEditableRow([existing], created);
    expect(created.uid).toBe("");
    expect(created.version).toBe(0);
    expect(isRowDirty(created)).toBe(true);
    expect(getEditableChanges(rows)).toEqual({
      created: [created],
      updated: [],
      deleted: [],
    });

    const removed = removeEditableRow(rows, "new");
    expect(removed).toEqual([existing]);
    expect(getEditableChanges(removed)).toEqual({
      created: [],
      updated: [],
      deleted: [],
    });
    expect(restoreEditableRow(removed, "new")).toEqual([existing]);
  });

  it("既有行修改后删除只派生 Delete，恢复后保留删除前草稿和原始版本", () => {
    const original = createEditableRow("a", "uid-a", 7, {
      name: "原值",
      quantity: 1,
    });
    const untouched = createEditableRow("b", "uid-b", 3, {
      name: "其他行",
      quantity: 1,
    });
    const changed = updateEditableRow([original, untouched], "a", {
      name: "草稿",
      quantity: 2,
    });
    const deleted = removeEditableRow(changed, "a");
    const changes = getEditableChanges(deleted);
    expect(changes.created).toHaveLength(0);
    expect(changes.updated).toHaveLength(0);
    expect(changes.deleted.map((row) => row.clientKey)).toEqual(["a"]);
    expect(getVisibleEditableRows(deleted)).toEqual([untouched]);
    expect(deleted[0]?.baseline).toEqual({ name: "原值", quantity: 1 });
    expect(deleted[0]?.current).toEqual({ name: "草稿", quantity: 2 });
    expect(deleted[0]?.uid).toBe("uid-a");
    expect(deleted[0]?.version).toBe(7);
    expect(
      updateEditableRow(deleted, "a", { quantity: 99 })[0]?.current.quantity,
    ).toBe(2);

    const restored = restoreEditableRow(deleted, "a");
    expect(getVisibleEditableRows(restored)).toHaveLength(2);
    expect(restored[0]?.current).toEqual({ name: "草稿", quantity: 2 });
    expect(
      getEditableChanges(restored).updated.map((row) => row.clientKey),
    ).toEqual(["a"]);
    expect(getEditableChanges(restored).deleted).toHaveLength(0);
    expect(original.current).toEqual({ name: "原值", quantity: 1 });
  });

  it("未改字段的既有行删除仍是业务变化，恢复后回到 clean", () => {
    const rows = [createEditableRow("a", "uid-a", 1, { name: "原值" })];
    const deleted = removeEditableRow(rows, "a");
    expect(deleted.some(isRowDirty)).toBe(true);
    expect(getEditableChanges(deleted).deleted).toHaveLength(1);
    const restored = restoreEditableRow(deleted, "a");
    expect(restored.some(isRowDirty)).toBe(false);
    expect(getEditableChanges(restored).updated).toHaveLength(0);
  });

  it("同一集合同时派生互斥增改删，放弃时移除新增并恢复既有行", () => {
    const rows = [
      createEditableRow("update", "uid-update", 2, { name: "待改原值" }),
      createEditableRow("delete", "uid-delete", 3, { name: "待删原值" }),
      createEditableRow("clean", "uid-clean", 4, { name: "未改" }),
      createCreatedEditableRow("create", { name: "新行" }),
    ];
    const changed = removeEditableRow(
      updateEditableRow(rows, "update", { name: "修改值" }),
      "delete",
    );
    const changes = getEditableChanges(changed);
    expect(changes.created.map((row) => row.clientKey)).toEqual(["create"]);
    expect(changes.updated.map((row) => row.clientKey)).toEqual(["update"]);
    expect(changes.deleted.map((row) => row.clientKey)).toEqual(["delete"]);
    expect(
      new Set(
        [...changes.created, ...changes.updated, ...changes.deleted].map(
          (row) => row.clientKey,
        ),
      ).size,
    ).toBe(3);

    const discarded = discardEditableRows(changed);
    expect(discarded.map((row) => row.clientKey)).toEqual([
      "update",
      "delete",
      "clean",
    ]);
    expect(discarded.some(isRowDirty)).toBe(false);
    expect(getVisibleEditableRows(discarded)).toHaveLength(3);
    expect(discarded[0]?.current).toEqual({ name: "待改原值" });
    expect(discarded[0]?.current).not.toBe(discarded[0]?.baseline);
    expect(discarded[1]?.version).toBe(3);
    expect(changed[0]?.current.name).toBe("修改值");
    expect(changed[1]?.deleted).toBe(true);
  });

  it("新增确认后采用真实 Uid/Version 并转为既有行，下一次修改派生 Update", () => {
    const newRow = createCreatedEditableRow("stable-client", { name: "新行" });
    const anotherDraft = createCreatedEditableRow("other", {
      name: "另一草稿",
    });
    const submitted = { name: "服务端确认值" };
    const accepted = acceptEditableRow(
      [newRow, anotherDraft],
      "stable-client",
      "actual-uid",
      5,
      submitted,
    );
    expect(accepted[0]?.clientKey).toBe("stable-client");
    expect(accepted[0]?.uid).toBe("actual-uid");
    expect(accepted[0]?.version).toBe(5);
    expect(accepted[0]?.origin).toBe("existing");
    expect(accepted[0] && isRowDirty(accepted[0])).toBe(false);
    expect(accepted[1]).toBe(anotherDraft);
    expect(getEditableChanges(accepted).created).toEqual([anotherDraft]);
    submitted.name = "外部修改";
    expect(accepted[0]?.baseline.name).toBe("服务端确认值");
    expect(accepted[0]?.current.name).toBe("服务端确认值");
    expect(accepted[0]?.baseline).not.toBe(accepted[0]?.current);

    const changed = updateEditableRow(accepted, "stable-client", {
      name: "再次修改",
    });
    expect(
      getEditableChanges(changed).updated.map((row) => row.clientKey),
    ).toEqual(["stable-client"]);
    expect(changed[0]?.version).toBe(5);
  });

  it("仅接纳已确认删除行，未确认删除和其他草稿继续保留", () => {
    const rows = [
      createEditableRow("a", "uid-a", 1, { name: "A" }),
      createEditableRow("b", "uid-b", 1, { name: "B" }),
      createEditableRow("c", "uid-c", 1, { name: "C" }),
    ];
    const deleted = removeEditableRow(removeEditableRow(rows, "a"), "b");
    expect(() =>
      acceptEditableRow(deleted, "a", "uid-a", 2, { name: "A" }),
    ).toThrow("删除行必须通过已确认删除回执接纳");
    const accepted = acceptDeletedEditableRow(deleted, "a");
    expect(accepted.map((row) => row.clientKey)).toEqual(["b", "c"]);
    expect(
      getEditableChanges(accepted).deleted.map((row) => row.clientKey),
    ).toEqual(["b"]);
    expect(acceptDeletedEditableRow(accepted, "c")).toEqual(accepted);
    expect(deleted).toHaveLength(3);
  });

  it("重复 ClientKey 不得因身份或操作不同成为重复提交项", () => {
    const existing = createEditableRow("same", "uid-a", 1, { name: "A" });
    const created = createCreatedEditableRow("same", { name: "新行" });
    expect(() => addEditableRow([existing], created)).toThrow("ClientKey 重复");
    expect(() => getEditableChanges([existing, created])).toThrow(
      "ClientKey 重复",
    );
    expect(existing.current.name).toBe("A");
  });

  it("复杂可写投影克隆后仍 clean，修改和放弃都不污染嵌套基线", () => {
    const source = {
      details: { name: "原值", tags: ["A", "B"] },
      date: new Date("2026-10-09T00:00:00Z"),
    };
    const row = createEditableRow("a", "uid-a", 1, source);
    source.details.name = "外部修改";
    source.details.tags.push("外部标签");
    expect(row.current.details.name).toBe("原值");
    expect(row.baseline.details.tags).toEqual(["A", "B"]);
    expect(isRowDirty(row)).toBe(false);

    const changed = updateEditableRow([row], "a", {
      details: { name: "草稿", tags: ["A", "B", "C"] },
    });
    expect(changed.some(isRowDirty)).toBe(true);
    expect(changed[0]?.baseline.details).toEqual({
      name: "原值",
      tags: ["A", "B"],
    });
    const discarded = discardEditableRows(changed);
    expect(discarded.some(isRowDirty)).toBe(false);
    expect(discarded[0]?.current.details).not.toBe(
      discarded[0]?.baseline.details,
    );
    const restored = updateEditableRow(changed, "a", {
      details: { tags: ["A", "B"], name: "原值" },
    });
    expect(restored.some(isRowDirty)).toBe(false);
  });

  it.each([
    [null, undefined],
    [undefined, null],
    [1, "1"],
    ["1", 1],
  ])("默认比较保留原始值区别：%s 与 %s", (baseline, current) => {
    const row = createEditableRow("a", "uid-a", 1, {
      value: baseline as string | number | null | undefined,
    });
    expect(
      updateEditableRow([row], "a", { value: current }).some(isRowDirty),
    ).toBe(true);
  });

  it("业务归一化由显式策略决定，策略随修改、放弃和接纳保留", () => {
    const row = createEditableRow(
      "a",
      "uid-a",
      1,
      { name: "原值", quantity: 1 },
      (baseline, current) =>
        baseline.name.trim() === current.name.trim() &&
        baseline.quantity === current.quantity,
    );
    const equivalent = updateEditableRow([row], "a", { name: " 原值 " });
    expect(equivalent.some(isRowDirty)).toBe(false);
    const changed = updateEditableRow(equivalent, "a", { quantity: 2 });
    expect(changed.some(isRowDirty)).toBe(true);
    expect(discardEditableRows(changed).some(isRowDirty)).toBe(false);
    const accepted = acceptEditableRow(changed, "a", "uid-a", 2, {
      name: " 原值 ",
      quantity: 2,
    });
    expect(
      updateEditableRow(accepted, "a", { name: "原值" }).some(isRowDirty),
    ).toBe(false);
  });
});
