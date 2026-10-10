import { saveSupplierBatch } from "../../../shared/api/generated/client";
import {
  getRequestFacts,
  RequestTransportError,
} from "../../../shared/api/请求执行";
import {
  ApiResponseError,
  describeApiError,
} from "../../../shared/api/响应错误";
import { acceptSupplierBatch } from "./model/提交契约";
import type {
  FrozenSuppliers,
  SupplierHttpResponse,
  SupplierSaveResult,
} from "./model/提交契约";

/** 单次调用生成操作且不自动重试，只有完整核验后返回整批成功结论。 */
export async function saveSuppliers(
  frozen: FrozenSuppliers,
  options?: RequestInit,
): Promise<SupplierSaveResult> {
  let response: SupplierHttpResponse | undefined;
  try {
    const headers = new Headers(options?.headers);
    headers.delete("Content-Type");
    response = await saveSupplierBatch(frozen.request, {
      ...options,
      // 当前生成器默认 JSON Patch；普通 DTO 显式覆盖，避免大小写重复媒体类型头。
      headers: {
        ...Object.fromEntries(headers.entries()),
        "Content-Type": "application/json",
      },
    });
    if (response.status !== 200)
      return {
        kind: "unknown",
        message: describeApiError(new ApiResponseError(response)),
        response,
        facts: getRequestFacts(response),
      };
    const acceptedRows = acceptSupplierBatch(frozen, response.data);
    return {
      kind: "confirmedSuccess",
      message: "供应商整批保存回执已完整核验。",
      acceptedRows,
      response,
      facts: getRequestFacts(response),
    };
  } catch (error) {
    // 尚无供应商独立短路验收证据，收到任何失败或不完整回执都保持 C 类限制。
    const notSent =
      error instanceof RequestTransportError &&
      error.kind === "not-sent" &&
      error.facts.phase === "not-sent";
    return {
      kind: notSent ? "confirmedNoEffect" : "unknown",
      message: describeApiError(error),
      response,
      facts:
        error instanceof RequestTransportError
          ? error.facts
          : response
            ? getRequestFacts(response)
            : undefined,
    };
  }
}
