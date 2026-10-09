/** 骨架验收专用字段；不会映射或发送真实 Product DTO。 */
export interface VerificationValue {
  name: string;
  quantity: number;
}
/** 本地模拟可控场景，用于验证客户端分类，不宣称后端具备对应能力。 */
export type SaveScenario =
  | "success"
  | "not-effective"
  | "business-unknown"
  | "lost-response"
  | "partial"
  | "slow";
/** 冻结项目始终保留本地行关联及提交时基线版本。 */
export interface FrozenItem extends VerificationValue {
  clientKey: string;
  uid: string;
  version: number;
  operation?: "create" | "update" | "delete";
}
/** 一个不可变的本地提交尝试；attemptId 仅作客户端关联。 */
export interface FrozenAttempt {
  attemptId: string;
  submittedAt: string;
  tabId: string;
  identityEpoch: number;
  userId: string;
  isNewDocument: boolean;
  items: readonly Readonly<FrozenItem>[];
}
/** 显式本地模拟回执，不以 HTTP 状态决定写入结果。 */
export type SimulationResult =
  | {
      kind: "confirmed";
      attemptId: string;
      items: {
        clientKey: string;
        uid: string;
        version: number;
        success: boolean;
        operation?: "create" | "update" | "delete";
      }[];
    }
  | { kind: "not-effective"; code: "SIMULATION_NO_EFFECT"; explanation: string }
  | {
      kind: "unknown";
      code: "SIMULATION_BUSINESS_UNKNOWN" | "SIMULATION_RESPONSE_LOST";
      explanation: string;
    };

/** 复制并冻结本次权威输入，后续回执不能读取另一页或后来变化的数据。 */
export function freezeAttempt(
  tabId: string,
  identityEpoch: number,
  userId: string,
  items: readonly FrozenItem[],
  isNewDocument = false,
): FrozenAttempt {
  return Object.freeze({
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
    tabId,
    identityEpoch,
    userId,
    isNewDocument,
    items: Object.freeze(items.map((item) => Object.freeze({ ...item }))),
  });
}

/** 模拟器完全在本地运行；延迟只模拟已派发后的等待，不用于展示就绪判断。 */
export async function simulateSave(
  attempt: FrozenAttempt,
  scenario: SaveScenario,
): Promise<SimulationResult> {
  await new Promise<void>((resolve) =>
    setTimeout(resolve, scenario === "slow" ? 2500 : 120),
  );
  if (scenario === "not-effective") {
    return {
      kind: "not-effective",
      code: "SIMULATION_NO_EFFECT",
      explanation: "本地模拟契约明确确认本次未生效，草稿保留。",
    };
  }
  if (scenario === "business-unknown" || scenario === "lost-response") {
    return {
      kind: "unknown",
      code:
        scenario === "business-unknown"
          ? "SIMULATION_BUSINESS_UNKNOWN"
          : "SIMULATION_RESPONSE_LOST",
      explanation:
        scenario === "business-unknown"
          ? "业务失败展示不能证明未生效，当前结果未知。"
          : "本地模拟响应丢失，当前结果未知。",
    };
  }
  return {
    kind: "confirmed",
    attemptId: attempt.attemptId,
    items: attempt.items.map((item, index) => ({
      clientKey: item.clientKey,
      uid:
        attempt.isNewDocument || item.operation === "create"
          ? crypto.randomUUID()
          : item.uid,
      version: item.version + 1,
      success: scenario !== "partial" || index === 0,
      operation: item.operation ?? "update",
    })),
  };
}

/** 核对完整覆盖、关联、身份和版本；遗漏、重复或错配均不能直接接纳。 */
export function isCompleteReceipt(
  attempt: FrozenAttempt,
  result: Extract<SimulationResult, { kind: "confirmed" }>,
): boolean {
  if (
    result.attemptId !== attempt.attemptId ||
    result.items.length !== attempt.items.length
  )
    return false;
  const keys = new Set<string>();
  return result.items.every((receipt) => {
    if (keys.has(receipt.clientKey)) return false;
    keys.add(receipt.clientKey);
    const submitted = attempt.items.find(
      (item) => item.clientKey === receipt.clientKey,
    );
    return (
      !!submitted &&
      (attempt.isNewDocument || submitted.operation === "create"
        ? /^[a-zA-Z0-9-]{1,80}$/.test(receipt.uid) &&
          receipt.uid !== submitted.uid
        : receipt.uid === submitted.uid) &&
      receipt.version === submitted.version + 1 &&
      (receipt.operation ?? "update") === (submitted.operation ?? "update")
    );
  });
}

/** 白名单保全本地模拟材料，不导出会话对象、token、认证头或不存在的 CommandId。 */
export function createRecoveryRecord(
  recordId: string,
  attempt: FrozenAttempt,
  result: SimulationResult,
) {
  return {
    recordId,
    businessType: "workspace-verification",
    interfaceOperation: "local.simulateSave (no HTTP request)",
    submittedAt: attempt.submittedAt,
    attemptId: attempt.attemptId,
    attemptIdMeaning: "仅用于本地关联，不是后端幂等键",
    frozenSubmission: attempt.items.map(
      ({ clientKey, uid, version, name, quantity, operation }) => ({
        ClientKey: clientKey,
        Uid: uid,
        Version: version,
        name,
        quantity,
        operation: operation ?? "update",
      }),
    ),
    response:
      result.kind === "confirmed"
        ? {
            kind: result.kind,
            attemptId: result.attemptId,
            items: result.items,
          }
        : {
            kind: result.kind,
            code: result.code,
            explanation: result.explanation,
          },
    pendingQuestions: [
      "本次本地模拟意图的结果仍未确认；不代表真实业务库发生写入。",
      "人工终止仅释放本地草稿，不证明失败、回滚或可安全重发。",
    ],
  };
}
