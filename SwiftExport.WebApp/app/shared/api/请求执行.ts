/** 请求执行的可观察阶段；已派发不表示服务器已经收到或提交。 */
export type RequestPhase = "not-sent" | "dispatched" | "response";

/** 保留传输事实供 feature 解释，不包含认证凭据或业务提交结论。 */
export interface RequestFacts {
  readonly path: string;
  readonly method: string;
  readonly phase: RequestPhase;
  readonly credentialRevision: number;
  readonly status?: number;
  readonly headers?: Headers;
  readonly rawBody?: string;
  readonly correlationId?: string;
}

/** 网络、取消或不可解析响应的事实错误；取消和网络故障不证明回滚。 */
export class RequestTransportError extends Error {
  constructor(
    public readonly kind:
      "not-sent" | "network" | "cancelled" | "invalid-response",
    public readonly facts: RequestFacts,
    options?: ErrorOptions,
  ) {
    super(
      kind === "not-sent"
        ? "请求尚未发送。"
        : kind === "cancelled"
          ? "请求已取消，服务器结果仍需核实。"
          : kind === "invalid-response"
            ? "响应无法解析，服务器结果仍需核实。"
            : "网络请求失败，服务器结果仍需核实。",
      options,
    );
    this.name = "RequestTransportError";
  }
}

/** 认证事件只传凭据版本和请求路径，用于过滤旧身份的迟到响应。 */
export interface AuthenticationEvent {
  readonly status: 401 | 403;
  readonly path: string;
  readonly credentialRevision: number;
}

let accessToken: string | null = null;
let credentialRevision = 0;
const listeners = new Set<(event: AuthenticationEvent) => void>();
const responseFacts = new WeakMap<object, RequestFacts>();

/** 只替换内存 Bearer；不访问浏览器持久化，不记录令牌。 */
export function setAccessToken(token: string | null): void {
  accessToken = token;
  credentialRevision += 1;
}

/** 返回不含凭据的版本号，用来忽略换身份前发出的认证错误。 */
export function getCredentialRevision(): number {
  return credentialRevision;
}

/** 会话层订阅认证失效；传输层本身不跳转页面或卸载工作区。 */
export function subscribeAuthenticationEvents(
  listener: (event: AuthenticationEvent) => void,
): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** 获取某次生成响应的完整传输事实；业务正文仍保留在生成的 data 中。 */
export function getRequestFacts(response: unknown): RequestFacts | undefined {
  return typeof response === "object" && response !== null
    ? responseFacts.get(response)
    : undefined;
}

/** Orval Fetch 唯一 mutator：注入内存 Bearer、透传信号，保留 HTTP 状态和完整正文。 */
export async function executeRequest<T>(
  path: string,
  options?: RequestInit,
): Promise<T> {
  const revision = credentialRevision;
  let facts: RequestFacts = {
    path,
    method: options?.method ?? "GET",
    phase: "not-sent",
    credentialRevision: revision,
  };
  let requestOptions: RequestInit;
  try {
    if (!path.startsWith("/api/") || options?.signal?.aborted) {
      throw new Error("只允许同源 API 路径，且请求信号必须有效。");
    }
    const headers = new Headers(options?.headers);
    // 登录请求不带旧 Bearer，其他操作只使用会话层持有的当前凭据。
    headers.delete("Authorization");
    if (accessToken && path !== "/api/auth/login")
      headers.set("Authorization", `Bearer ${accessToken}`);
    requestOptions = { ...options, headers, credentials: "omit" };
  } catch (cause) {
    throw new RequestTransportError("not-sent", facts, { cause });
  }

  let response: Response;
  try {
    facts = { ...facts, phase: "dispatched" };
    response = await fetch(path, requestOptions);
  } catch (cause) {
    const cancelled = cause instanceof Error && cause.name === "AbortError";
    throw new RequestTransportError(
      cancelled ? "cancelled" : "network",
      facts,
      { cause },
    );
  }
  facts = {
    ...facts,
    phase: "response",
    status: response.status,
    headers: response.headers,
    correlationId: response.headers.get("X-Correlation-ID") ?? undefined,
  };
  if (
    (response.status === 401 || response.status === 403) &&
    path !== "/api/auth/login"
  ) {
    const event: AuthenticationEvent = {
      status: response.status,
      path,
      credentialRevision: revision,
    };
    for (const listener of listeners)
      queueMicrotask(() => {
        if (listeners.has(listener)) listener(event);
      });
  }
  let body: string;
  try {
    body = [204, 205, 304].includes(response.status)
      ? ""
      : await response.text();
  } catch (cause) {
    throw new RequestTransportError(
      cause instanceof Error && cause.name === "AbortError"
        ? "cancelled"
        : "network",
      facts,
      { cause },
    );
  }
  facts = { ...facts, rawBody: body };
  let data: unknown;
  try {
    data = body ? JSON.parse(body) : undefined;
  } catch (cause) {
    throw new RequestTransportError("invalid-response", facts, { cause });
  }
  // 断言仅位于生成器委托的传输边界；不解包 ServiceResult，也不推断业务成功。
  const result = { data, status: response.status, headers: response.headers };
  responseFacts.set(result, facts);
  return result as T;
}
