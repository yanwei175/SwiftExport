import {
  saveDictionaryTypes,
  saveDictionaryItems as requestSaveDictionaryItems,
} from "../../../shared/api/generated/client";
import {
  getRequestFacts,
  RequestTransportError,
} from "../../../shared/api/请求执行";
import {
  ApiResponseError,
  describeApiError,
} from "../../../shared/api/响应错误";
import {
  acceptDictionaryTypeBatch,
  acceptDictionaryItemsBatch,
} from "./model/提交契约";
import type {
  FrozenDictionaryType,
  FrozenDictionaryItems,
  DictionaryHttpResponse,
  DictionarySaveEvidence,
  DictionaryTypeSaveResult,
  DictionaryItemsSaveResult,
} from "./model/提交契约";

/** 类型始终调用生成操作；禁止自动重试，完整回执接纳后才返回 A。 */
export async function saveDictionaryType(
  frozen: FrozenDictionaryType,
  options?: RequestInit,
): Promise<DictionaryTypeSaveResult> {
  let response: DictionaryHttpResponse | undefined;
  try {
    response = await saveDictionaryTypes(frozen.request, jsonOptions(options));
    const evidence = responseEvidence(response);
    if (response.status !== 200)
      return {
        ...evidence,
        kind: isConfirmedShortCircuit(response)
          ? "confirmedNoEffect"
          : "unknown",
      };
    return {
      ...evidence,
      kind: "confirmedSuccess",
      acceptedType: acceptDictionaryTypeBatch(frozen, response.data),
    };
  } catch (error) {
    return {
      ...failureEvidence(error, response),
      kind: isNotSent(error) ? "confirmedNoEffect" : "unknown",
    };
  }
}

/** 字典项真实契约为整批原子提交；不会接纳只有若干行成功的不完整回执。 */
export async function saveDictionaryItems(
  frozen: FrozenDictionaryItems,
  options?: RequestInit,
): Promise<DictionaryItemsSaveResult> {
  let response: DictionaryHttpResponse | undefined;
  try {
    response = await requestSaveDictionaryItems(
      frozen.request,
      jsonOptions(options),
    );
    const evidence = responseEvidence(response);
    if (response.status !== 200)
      return {
        ...evidence,
        kind: isConfirmedShortCircuit(response)
          ? "confirmedNoEffect"
          : "unknown",
      };
    return {
      ...evidence,
      kind: "confirmedSuccess",
      acceptedRows: acceptDictionaryItemsBatch(frozen, response.data),
    };
  } catch (error) {
    return {
      ...failureEvidence(error, response),
      kind: isNotSent(error) ? "confirmedNoEffect" : "unknown",
    };
  }
}

/** Swagger 同时列出了 JSON Patch 媒体类型；本 feature 提交普通 DTO 时明确发送 application/json。 */
function jsonOptions(options: RequestInit | undefined): RequestInit {
  const headers = new Headers(options?.headers);
  headers.delete("Content-Type");
  // Orval 默认键为 Content-Type；Headers 会变成小写键，直接合并会形成两种 MIME 的重复头。
  return {
    ...options,
    headers: {
      ...Object.fromEntries(headers.entries()),
      "Content-Type": "application/json",
    },
  };
}

/** 限定当前两个端点的前置短路机器码证据，不按任意 4xx 状态猜测未写入。 */
function isConfirmedShortCircuit(response: DictionaryHttpResponse): boolean {
  const facts = getRequestFacts(response);
  const body = response.data;
  if (
    !facts ||
    facts.phase !== "response" ||
    !["/api/sysdicttype/batch-save", "/api/sysdictdata/batch-save"].includes(
      facts.path,
    ) ||
    !isRecord(body) ||
    body.success !== false ||
    body.data != null ||
    !isRecord(body.error)
  )
    return false;
  const code = body.error.code;
  // 字典服务本身不返回以下代码；对应绑定/权限/防重复过滤器在 Action 前拒绝。
  return (
    (response.status === 400 && code === "VALIDATION") ||
    (response.status === 403 && code === "FORBIDDEN") ||
    (response.status === 429 && code === "RATE_LIMITED")
  );
}

/** 请求确定未派发是 B；已派发取消、超时和网络故障全部保守为 C。 */
function isNotSent(error: unknown): boolean {
  return (
    error instanceof RequestTransportError &&
    error.kind === "not-sent" &&
    error.facts.phase === "not-sent"
  );
}

/** 显示服务端结构化说明不等于生效分类；保留当前统一执行边界的事实引用。 */
function responseEvidence(
  response: DictionaryHttpResponse,
): DictionarySaveEvidence {
  return {
    response,
    facts: getRequestFacts(response),
    message:
      response.status === 200
        ? "字典保存回执已返回。"
        : describeApiError(new ApiResponseError(response)),
  };
}

/** 解析失败或完整核验失败仍保留响应/追踪；不得把 catch 分支误当未发送。 */
function failureEvidence(
  error: unknown,
  response: DictionaryHttpResponse | undefined,
): DictionarySaveEvidence {
  return {
    response,
    facts:
      error instanceof RequestTransportError
        ? error.facts
        : response
          ? getRequestFacts(response)
          : undefined,
    message: describeApiError(error),
  };
}

/** HTTP 正文以运行时对象检查，不依赖生成器仅声明 200 的类型假设。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
