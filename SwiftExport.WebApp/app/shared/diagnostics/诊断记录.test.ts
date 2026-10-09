import { beforeEach, describe, expect, it } from "vitest";
import {
  clearDiagnostics,
  getDiagnosticSnapshot,
  recordDiagnostic,
} from "./诊断记录";

beforeEach(() => clearDiagnostics());

describe("内存诊断白名单", () => {
  it("只复制元数据，运行时额外凭据、正文和异常文本均不进入记录", () => {
    const input = {
      event: "request-failed",
      level: "error",
      operationId: "productsBatchSave",
      status: 503,
      phase: "dispatched",
      kind: "network",
      correlationId: "trace-abc",
      token: "sensitive-value",
      headers: { Authorization: "sensitive-value" },
      rawBody: "sensitive-value",
      message: "sensitive-value",
      stack: "sensitive-value",
      data: { password: "sensitive-value" },
    } as const;
    recordDiagnostic(input);
    const result = getDiagnosticSnapshot();
    expect(result[0]).toMatchObject({
      operationId: "productsBatchSave",
      status: 503,
      phase: "dispatched",
      correlationId: "trace-abc",
    });
    expect(JSON.stringify(result)).not.toContain("sensitive-value");
    expect(Object.isFrozen(result)).toBe(true);
    expect(Object.isFrozen(result[0])).toBe(true);
  });

  it("带查询参数的URL、换行追踪和非法数字不进入允许字段", () => {
    recordDiagnostic({
      event: "request-failed",
      level: "warn",
      operationId: "/api/users?password=sensitive",
      correlationId: "trace\nsensitive",
      durationMs: Number.NaN,
      status: 42,
    });
    expect(getDiagnosticSnapshot()[0]).toMatchObject({
      operationId: undefined,
      correlationId: undefined,
      durationMs: undefined,
      status: undefined,
    });
  });

  it("第101条淘汰最早条，清理不改变已经返回的不可变快照", () => {
    for (let index = 0; index < 101; index++)
      recordDiagnostic({
        event: "save-outcome",
        level: "warn",
        attemptId: `attempt-${index}`,
        outcome: "C",
      });
    const snapshot = getDiagnosticSnapshot();
    expect(snapshot).toHaveLength(100);
    expect(snapshot[0]!.attemptId).toBe("attempt-1");
    expect(snapshot[99]!.attemptId).toBe("attempt-100");
    clearDiagnostics();
    expect(getDiagnosticSnapshot()).toEqual([]);
    expect(snapshot).toHaveLength(100);
  });
});
