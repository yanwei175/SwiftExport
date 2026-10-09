import type { ParsedInput } from "../../../shared/editing/未提交输入";
import {
  parseIntegerText,
  parseDecimalText,
} from "../../../shared/format/数值转换";
import { parseBusinessDate } from "../../../shared/format/日期转换";

/** 验收单据头只保存可写字段投影；日期与金额均保留明确的业务文本语义。 */
export interface DocumentHeader {
  name: string;
  amount: string | null;
  businessDate: string | null;
}
/** 验收明细可写字段；身份及版本由编辑集合元数据持有。 */
export interface DocumentLine {
  name: string;
  quantity: number;
  kind: "normal" | "fragile";
  businessDate: string | null;
  categoryId: string | null;
}
/** 本地验收协议明确冻结整个头行集合，不代表真实业务 BatchSave 都是原子操作。 */
export interface FrozenDocument {
  readonly attemptId: string;
  readonly submittedAt: string;
  readonly tabId: string;
  readonly identityEpoch: number;
  readonly userId: string;
  readonly header: Readonly<DocumentHeader & { uid: string; version: number }>;
  readonly lines: readonly Readonly<
    DocumentLine & { clientKey: string; uid: string; version: number }
  >[];
}
/** 专用头行验收只模拟原子 A/B/C；原列表验收的行级部分成功契约继续独立存在。 */
export type DocumentScenario =
  "success" | "not-effective" | "business-unknown" | "lost-response" | "slow";
/** 明确关联到同一个冻结头行请求的模拟回执。 */
export type DocumentResult =
  | {
      kind: "confirmed";
      attemptId: string;
      header: { uid: string; version: number };
      lines: { clientKey: string; uid: string; version: number }[];
    }
  | { kind: "not-effective"; code: "SIMULATION_NO_EFFECT"; explanation: string }
  | {
      kind: "unknown";
      code: "SIMULATION_BUSINESS_UNKNOWN" | "SIMULATION_RESPONSE_LOST";
      explanation: string;
    };

/** 将控件原始文本转换为该字段的业务值；不改变缓冲、集合或当前值归属。 */
export function parseDocumentLineField(
  field: keyof DocumentLine,
  raw: string,
): ParsedInput<DocumentLine[keyof DocumentLine]> {
  if (field === "quantity") {
    const parsed = parseIntegerText(raw);
    return parsed.valid && parsed.value !== null && parsed.value > 0
      ? { valid: true, value: parsed.value }
      : { valid: false };
  }
  if (field === "kind")
    return raw === "normal" || raw === "fragile"
      ? { valid: true, value: raw }
      : { valid: false };
  if (field === "businessDate") return parseBusinessDate(raw, true);
  if (field === "categoryId") return { valid: true, value: raw || null };
  return raw.trim() ? { valid: true, value: raw } : { valid: false };
}

/** Form 读取后的头字段投影；不把原始数字文本强制变零，不保存 Dayjs 对象。 */
export function projectDocumentHeader(
  name: string,
  amount: string,
  businessDate: string | null,
): ParsedInput<DocumentHeader> {
  const money = parseDecimalText(amount, true);
  const date = parseBusinessDate(businessDate, true);
  return name.trim() && money.valid && date.valid
    ? {
        valid: true,
        value: { name, amount: money.value, businessDate: date.value },
      }
    : { valid: false };
}

/** 深复制并冻结同一次头行提交；后续 UI 变化不能改变回执对账材料。 */
export function freezeDocument(
  input: Omit<FrozenDocument, "attemptId" | "submittedAt">,
): FrozenDocument {
  return Object.freeze({
    ...input,
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
    header: Object.freeze({ ...input.header }),
    lines: Object.freeze(input.lines.map((line) => Object.freeze({ ...line }))),
  });
}

/** 本地模拟不发送 HTTP；延迟仅模拟等待回执，不作为展示握手成功的证据。 */
export async function simulateDocumentSave(
  attempt: FrozenDocument,
  scenario: DocumentScenario,
): Promise<DocumentResult> {
  await new Promise<void>((resolve) =>
    setTimeout(resolve, scenario === "slow" ? 2500 : 120),
  );
  if (scenario === "not-effective")
    return {
      kind: "not-effective",
      code: "SIMULATION_NO_EFFECT",
      explanation: "本地验收协议确认头行均未生效，保留草稿并解除保存锁。",
    };
  if (scenario === "business-unknown" || scenario === "lost-response")
    return {
      kind: "unknown",
      code:
        scenario === "business-unknown"
          ? "SIMULATION_BUSINESS_UNKNOWN"
          : "SIMULATION_RESPONSE_LOST",
      explanation: "本地验收无法确认头行是否生效；保留冻结材料，不自动重发。",
    };
  return {
    kind: "confirmed",
    attemptId: attempt.attemptId,
    header: { uid: attempt.header.uid, version: attempt.header.version + 1 },
    lines: attempt.lines.map((line) => ({
      clientKey: line.clientKey,
      uid: line.uid,
      version: line.version + 1,
    })),
  };
}

/** 只有头与每一行的完整关联、身份和版本都匹配，才允许一次性接纳整个本地验收会话。 */
export function isCompleteDocumentReceipt(
  attempt: FrozenDocument,
  result: Extract<DocumentResult, { kind: "confirmed" }>,
): boolean {
  if (
    result.attemptId !== attempt.attemptId ||
    result.header.uid !== attempt.header.uid ||
    result.header.version !== attempt.header.version + 1 ||
    result.lines.length !== attempt.lines.length
  )
    return false;
  const seen = new Set<string>();
  return result.lines.every((receipt) => {
    if (seen.has(receipt.clientKey)) return false;
    seen.add(receipt.clientKey);
    const line = attempt.lines.find(
      (item) => item.clientKey === receipt.clientKey,
    );
    return (
      !!line && line.uid === receipt.uid && receipt.version === line.version + 1
    );
  });
}

/** 白名单导出核实所需头行材料；不接受会话对象、凭据或不存在的服务端 CommandId。 */
export function createDocumentRecoveryRecord(
  recordId: string,
  attempt: FrozenDocument,
  result: DocumentResult,
) {
  return {
    recordId,
    businessType: "document-verification",
    interfaceOperation: "local.simulateDocumentSave (no HTTP request)",
    submittedAt: attempt.submittedAt,
    attemptId: attempt.attemptId,
    attemptIdMeaning: "仅用于本地关联，不是后端幂等键",
    frozenSubmission: {
      header: {
        Uid: attempt.header.uid,
        Version: attempt.header.version,
        name: attempt.header.name,
        amount: attempt.header.amount,
        businessDate: attempt.header.businessDate,
      },
      lines: attempt.lines.map(({ clientKey, uid, version, ...values }) => ({
        ClientKey: clientKey,
        Uid: uid,
        Version: version,
        ...values,
      })),
    },
    response: structuredClone(result),
    pendingQuestions: [
      "本地模拟头行结果仍未确认，不代表真实业务库发生写入。",
      "人工终止仅释放本地草稿，不证明失败、回滚或可安全重发。",
    ],
  };
}
