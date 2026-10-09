import { describe, expect, it } from "vitest";
import {
  createRecoveryRecord,
  freezeAttempt,
  isCompleteReceipt,
  simulateSave,
} from "./模拟保存";

describe("本地模拟回执关联与恢复材料", () => {
  it("混合增改删逐项核对操作，新行接纳新身份，错操作保持无法接纳", async () => {
    const attempt = freezeAttempt("tab-grid", 1, "user-a", [
      {
        clientKey: "new",
        uid: "",
        version: 0,
        name: "新增",
        quantity: 1,
        operation: "create",
      },
      {
        clientKey: "existing",
        uid: "existing",
        version: 5,
        name: "删除前草稿",
        quantity: 1,
        operation: "delete",
      },
    ]);
    const result = await simulateSave(attempt, "success");
    if (result.kind !== "confirmed") throw new Error("本地成功场景缺少回执");
    expect(isCompleteReceipt(attempt, result)).toBe(true);
    expect(result.items[0]?.uid).toMatch(/^[a-f0-9-]{36}$/);
    expect(result.items[0]?.version).toBe(1);
    expect(result.items[1]).toMatchObject({
      uid: "existing",
      operation: "delete",
      version: 6,
    });
    expect(
      isCompleteReceipt(attempt, {
        ...result,
        items: result.items.map((item) =>
          item.clientKey === "existing"
            ? { ...item, operation: "update" }
            : item,
        ),
      }),
    ).toBe(false);
  });
  it("新单回执给出新实体Uid，遗漏实体身份不能被接纳", async () => {
    const attempt = freezeAttempt(
      "tab-a",
      1,
      "user-a",
      [
        {
          clientKey: "a",
          uid: "temporary-draft",
          version: 1,
          name: "新单",
          quantity: 1,
        },
      ],
      true,
    );
    const result = await simulateSave(attempt, "success");
    expect(result.kind).toBe("confirmed");
    if (result.kind !== "confirmed") throw new Error("本地成功场景缺少回执");
    expect(isCompleteReceipt(attempt, result)).toBe(true);
    expect(result.items[0]?.uid).not.toBe("temporary-draft");
    expect(
      isCompleteReceipt(attempt, {
        ...result,
        items: [{ ...result.items[0]!, uid: "" }],
      }),
    ).toBe(false);
    expect(
      isCompleteReceipt(attempt, {
        ...result,
        items: [{ ...result.items[0]!, uid: "temporary-draft" }],
      }),
    ).toBe(false);
  });
  it("冻结内容独立，遗漏和重复回执不能作为完整成功", () => {
    const items = [
      { clientKey: "a", uid: "a", version: 1, name: "原值", quantity: 1 },
    ];
    const attempt = freezeAttempt("tab-a", 1, "user-a", items);
    items[0]!.name = "后来值";
    expect(attempt.items[0]?.name).toBe("原值");
    expect(
      isCompleteReceipt(attempt, {
        kind: "confirmed",
        attemptId: attempt.attemptId,
        items: [],
      }),
    ).toBe(false);
    const receipt = { clientKey: "a", uid: "a", version: 2, success: true };
    expect(
      isCompleteReceipt(attempt, {
        kind: "confirmed",
        attemptId: attempt.attemptId,
        items: [receipt],
      }),
    ).toBe(true);
    expect(
      isCompleteReceipt(attempt, {
        kind: "confirmed",
        attemptId: attempt.attemptId,
        items: [receipt, receipt],
      }),
    ).toBe(false);
  });

  it("保全记录与文件标识一致，输出仅有核实白名单", () => {
    const attempt = freezeAttempt("tab-a", 1, "user-a", [
      { clientKey: "a", uid: "a", version: 1, name: "原值", quantity: 1 },
    ]);
    const record = createRecoveryRecord("record-a", attempt, {
      kind: "unknown",
      code: "SIMULATION_RESPONSE_LOST",
      explanation: "未知",
    });
    expect(record.recordId).toBe("record-a");
    expect(record.attemptId).toBe(attempt.attemptId);
    expect(record.frozenSubmission[0]?.Version).toBe(1);
    expect(JSON.stringify(record)).not.toMatch(
      /authorization|token|password|commandId/i,
    );
  });
});
