import { describe, expect, it, vi } from "vitest";
import {
  acceptEditableRow,
  createEditableRow,
  discardEditableRows,
  isRowDirty,
  updateEditableRow,
} from "../../../shared/editing/编辑集合";
import {
  createDocumentRecoveryRecord,
  freezeDocument,
  isCompleteDocumentReceipt,
  parseDocumentLineField,
  projectDocumentHeader,
  simulateDocumentSave,
} from "./头行模拟保存";

/** 定向证明统一冻结、完整回执及 A/B/C 不会被某个头或某一行的结果替代。 */
describe("头行验收契约", () => {
  const input = () => ({
    tabId: "tab-a",
    identityEpoch: 1,
    userId: "user-a",
    header: {
      uid: "doc-1",
      version: 1,
      name: "头",
      amount: "0",
      businessDate: null,
    },
    lines: [
      {
        clientKey: "line-a",
        uid: "uid-a",
        version: 4,
        name: "行",
        referenceCode: "REF-A",
        quantity: 2,
        kind: "normal" as const,
        businessDate: null,
        categoryId: null,
      },
    ],
  });
  it("数字/null/日期转换不把未完成输入送入业务值", () => {
    expect(parseDocumentLineField("quantity", "-")).toEqual({ valid: false });
    expect(parseDocumentLineField("quantity", "0")).toEqual({ valid: false });
    expect(parseDocumentLineField("referenceCode", "")).toEqual({
      valid: false,
    });
    expect(parseDocumentLineField("referenceCode", "REF-RAW-A")).toEqual({
      valid: true,
      value: "REF-RAW-A",
    });
    expect(parseDocumentLineField("businessDate", "")).toEqual({
      valid: true,
      value: null,
    });
    expect(projectDocumentHeader("头", "", null)).toEqual({
      valid: true,
      value: { name: "头", amount: null, businessDate: null },
    });
    expect(projectDocumentHeader("头", "1.", null)).toEqual({ valid: false });
  });
  it("冻结与完整接纳包含头版本和所有行，遗漏或错配均不接纳", () => {
    const source = input();
    const attempt = freezeDocument(source);
    source.header.name = "后改";
    source.lines[0]!.name = "后改行";
    source.lines[0]!.referenceCode = "REF-LATER";
    expect(attempt.header.name).toBe("头");
    expect(attempt.lines[0]!.name).toBe("行");
    expect(attempt.lines[0]!.referenceCode).toBe("REF-A");
    const result = {
      kind: "confirmed" as const,
      attemptId: attempt.attemptId,
      header: { uid: "doc-1", version: 2 },
      lines: [{ clientKey: "line-a", uid: "uid-a", version: 5 }],
    };
    expect(isCompleteDocumentReceipt(attempt, result)).toBe(true);
    expect(isCompleteDocumentReceipt(attempt, { ...result, lines: [] })).toBe(
      false,
    );
    expect(
      isCompleteDocumentReceipt(attempt, {
        ...result,
        header: { ...result.header, version: 1 },
      }),
    ).toBe(false);
    expect(
      isCompleteDocumentReceipt(attempt, {
        ...result,
        lines: [{ ...result.lines[0]!, uid: "other" }],
      }),
    ).toBe(false);
    const record = createDocumentRecoveryRecord("record", attempt, {
      kind: "unknown",
      code: "SIMULATION_RESPONSE_LOST",
      explanation: "丢失",
    });
    expect(record.frozenSubmission.lines[0]).toMatchObject({
      ClientKey: "line-a",
      Version: 4,
      referenceCode: "REF-A",
    });
    expect(JSON.stringify(record)).not.toMatch(
      /Authorization|access_token|CommandId|password/,
    );
  });
  it("未显示的业务字段参与修改、放弃与确认接纳，身份和基线仍独立", () => {
    const { clientKey, uid, version, ...values } = input().lines[0]!;
    const initial = createEditableRow(clientKey, uid, version, values);
    const changed = updateEditableRow([initial], clientKey, {
      name: "关联名称",
      referenceCode: "REF-CHANGED",
    });
    expect(isRowDirty(changed[0]!)).toBe(true);
    expect(changed[0]).toMatchObject({ clientKey, uid, version });
    expect(changed[0]!.baseline.referenceCode).toBe("REF-A");
    expect(discardEditableRows(changed)[0]!.current.referenceCode).toBe(
      "REF-A",
    );
    const accepted = acceptEditableRow(
      changed,
      clientKey,
      uid,
      version + 1,
      changed[0]!.current,
    );
    expect(accepted[0]!.baseline.referenceCode).toBe("REF-CHANGED");
    expect(isRowDirty(accepted[0]!)).toBe(false);
    expect(discardEditableRows(accepted)[0]!.current.referenceCode).toBe(
      "REF-CHANGED",
    );
  });
  it("本地模拟分别确认成功、未生效与未知，不宣称后端已具备", async () => {
    vi.useFakeTimers();
    try {
      const attempt = freezeDocument(input());
      for (const [scenario, kind] of [
        ["success", "confirmed"],
        ["not-effective", "not-effective"],
        ["business-unknown", "unknown"],
        ["lost-response", "unknown"],
      ] as const) {
        const promise = simulateDocumentSave(attempt, scenario);
        await vi.runAllTimersAsync();
        expect((await promise).kind).toBe(kind);
      }
    } finally {
      vi.useRealTimers();
    }
  });
});
