/** 可诊断的固定事件；不接收自由文本、任意业务 payload 或原始异常对象。 */
export type DiagnosticEventName =
  | "request-failed"
  | "permission-changed"
  | "save-outcome"
  | "render-failed"
  | "preference-persist-failed"
  | "session-restore-failed";

/** 固定错误类别用于排查；不复制异常 message、stack、请求或响应正文。 */
export type DiagnosticKind =
  | "not-sent"
  | "network"
  | "cancelled"
  | "invalid-response"
  | "unexpected"
  | "storage-unavailable"
  | "invalid-record";

/** 调用方只提供允许的元数据；operationId 是固定操作名，不能使用带参数的 URL。 */
export interface DiagnosticInput {
  readonly event: DiagnosticEventName;
  readonly level: "info" | "warn" | "error";
  readonly buildVersion?: string;
  readonly pageKind?: string;
  readonly tabId?: string;
  readonly operationId?: string;
  readonly status?: number;
  readonly phase?: "not-sent" | "dispatched" | "response";
  readonly kind?: DiagnosticKind;
  readonly durationMs?: number;
  readonly attemptId?: string;
  readonly outcome?: "A" | "B" | "C";
  readonly correlationId?: string;
}

/** 内存记录的标识与 UTC 时间由公共入口生成，不含身份凭据或业务内容。 */
export interface DiagnosticRecord extends DiagnosticInput {
  readonly eventId: string;
  readonly timestamp: string;
}

const events = new Set<DiagnosticEventName>([
  "request-failed",
  "permission-changed",
  "save-outcome",
  "render-failed",
  "preference-persist-failed",
  "session-restore-failed",
]);
const kinds = new Set<DiagnosticKind>([
  "not-sent",
  "network",
  "cancelled",
  "invalid-response",
  "unexpected",
  "storage-unavailable",
  "invalid-record",
]);
const records: DiagnosticRecord[] = [];
let sequence = 0;

/** 标识只允许常见操作/追踪字符，拒绝 URL 查询、空白及换行携带的额外内容。 */
function identifier(value: unknown): string | undefined {
  return typeof value === "string" && /^[\w.:-]+$/.test(value)
    ? value
    : undefined;
}

/** 每次只复制白名单字段；即使运行时传入额外属性，也不会进入内存记录。 */
export function recordDiagnostic(input: DiagnosticInput): void {
  if (
    !events.has(input.event) ||
    !["info", "warn", "error"].includes(input.level)
  )
    return;
  const record: DiagnosticRecord = Object.freeze({
    eventId: `diagnostic-${++sequence}`,
    timestamp: new Date().toISOString(),
    event: input.event,
    level: input.level,
    buildVersion: identifier(input.buildVersion),
    pageKind: identifier(input.pageKind),
    tabId: identifier(input.tabId),
    operationId: identifier(input.operationId),
    status:
      Number.isInteger(input.status) &&
      input.status! >= 100 &&
      input.status! <= 599
        ? input.status
        : undefined,
    phase: ["not-sent", "dispatched", "response"].includes(input.phase ?? "")
      ? input.phase
      : undefined,
    kind: input.kind && kinds.has(input.kind) ? input.kind : undefined,
    durationMs:
      typeof input.durationMs === "number" &&
      Number.isFinite(input.durationMs) &&
      input.durationMs >= 0
        ? input.durationMs
        : undefined,
    attemptId: identifier(input.attemptId),
    outcome: ["A", "B", "C"].includes(input.outcome ?? "")
      ? input.outcome
      : undefined,
    correlationId: identifier(input.correlationId),
  });
  records.push(record);
  if (records.length > 100) records.shift();
}

/** 返回只读白名单快照，供用户主动查看/导出；不会自动上传或持久化。 */
export function getDiagnosticSnapshot(): readonly DiagnosticRecord[] {
  return Object.freeze([...records]);
}

/** 登出或换身份时清理当前浏览器会话的诊断，避免下一身份继承记录。 */
export function clearDiagnostics(): void {
  records.length = 0;
}
