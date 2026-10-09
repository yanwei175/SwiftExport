import { getRequestFacts } from "./请求执行";
import type { RequestFacts } from "./请求执行";

/** 某个 feature 无法消费的响应；保留原始正文供该用例核实，不代表未写入。 */
export class ApiResponseError extends Error {
  readonly facts: RequestFacts | undefined;

  constructor(
    public readonly response: {
      status: number;
      data: unknown;
      headers: Headers;
    },
    message = "请求返回了无法确认的结果。",
  ) {
    super(message);
    this.name = "ApiResponseError";
    this.facts = getRequestFacts(response);
  }
}

/** 仅用于展示服务端说明；不分析文字判断回滚、版本冲突或业务成功。 */
export function describeApiError(error: unknown): string {
  if (error instanceof ApiResponseError) {
    const body = error.response.data;
    if (typeof body === "object" && body !== null && "error" in body) {
      const detail = body.error;
      if (
        typeof detail === "object" &&
        detail !== null &&
        "message" in detail &&
        typeof detail.message === "string"
      )
        return detail.message;
    }
    return `${error.message}（HTTP ${error.response.status}）`;
  }
  return error instanceof Error
    ? error.message
    : "请求失败，请核对网络与账号状态。";
}
