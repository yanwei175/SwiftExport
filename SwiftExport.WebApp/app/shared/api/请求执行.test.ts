import { afterEach, describe, expect, it, vi } from "vitest";
import {
  getDictionaryByCode,
  getMe,
  getProductWarehouseCandidates,
  login,
  getBrowserSessionCsrf,
  restoreBrowserSession,
  logoutBrowserSession,
  saveProductBatch,
} from "./generated/client";
import {
  getRequestFacts,
  RequestTransportError,
  setAccessToken,
  subscribeAuthenticationEvents,
} from "./请求执行";
import {
  clearDiagnostics,
  getDiagnosticSnapshot,
} from "../diagnostics/诊断记录";

/** 构造模拟 HTTP 正文；只验证传输接线，不连接实际后端或数据库。 */
function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      "X-Correlation-ID": "api-test",
      "Retry-After": "5",
    },
  });
}

afterEach(() => {
  setAccessToken(null);
  clearDiagnostics();
  vi.restoreAllMocks();
});

describe("生成 Fetch 的统一请求事实", () => {
  it("Cookie 会话仅四个显式入口使用，普通业务不发送 Cookie 或混用 Bearer", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async () => response({}));
    setAccessToken("old-secret");
    await getBrowserSessionCsrf();
    await restoreBrowserSession();
    await logoutBrowserSession({ headers: { "X-CSRF-TOKEN": "csrf" } });
    await login(
      { userName: "x", password: "x" },
      {
        headers: {
          "X-SwiftExport-Web-Session": "1",
          Authorization: "old-secret",
        },
      },
    );
    for (const [, options] of fetch.mock.calls) {
      expect(options?.credentials).toBe("same-origin");
      expect(new Headers(options?.headers).has("Authorization")).toBe(false);
    }
    await getMe({ credentials: "include" });
    expect(fetch.mock.calls[4]?.[1]?.credentials).toBe("omit");
    expect(
      new Headers(fetch.mock.calls[4]?.[1]?.headers).get("Authorization"),
    ).toBe("Bearer old-secret");
  });

  it("请求诊断只保留审核操作和追踪事实，不包含参数、正文或凭据", async () => {
    setAccessToken("secret-token");
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      response(
        { password: "secret-body", error: { message: "private-business" } },
        500,
      ),
    );
    await getDictionaryByCode("private-lookup-value");
    expect(getDiagnosticSnapshot()).toHaveLength(1);
    expect(getDiagnosticSnapshot()[0]).toMatchObject({
      operationId: "getDictionaryByCode",
      status: 500,
      phase: "response",
      correlationId: "api-test",
    });
    expect(JSON.stringify(getDiagnosticSnapshot())).not.toMatch(
      /secret-token|secret-body|private-business|private-lookup-value|Authorization/,
    );
  });
  it("认证裸 DTO 不解包，登录不发送旧令牌，me 自动携带当前 Bearer", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(response({ access_token: "new-token" }))
      .mockResolvedValueOnce(
        response({ userId: "user-1", permissions: ["Products.Product.View"] }),
      );
    setAccessToken("old-token");
    const loginResult = await login({ userName: "demo", password: "secret" });
    expect(loginResult.status).toBe(200);
    expect(loginResult.data).toEqual({ access_token: "new-token" });
    expect(
      new Headers(fetch.mock.calls[0]?.[1]?.headers).has("Authorization"),
    ).toBe(false);
    setAccessToken("new-token");
    const me = await getMe();
    expect(me.data).toMatchObject({ userId: "user-1" });
    expect(
      new Headers(fetch.mock.calls[1]?.[1]?.headers).get("Authorization"),
    ).toBe("Bearer new-token");
    expect(getRequestFacts(me)).toMatchObject({
      phase: "response",
      status: 200,
      correlationId: "api-test",
    });
  });

  it("字典路径及查询参数编码保持生成契约，AbortSignal 原样透传", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async () => response({ success: true, data: [] }));
    const controller = new AbortController();
    await getDictionaryByCode("Unit / 中文", { signal: controller.signal });
    expect(fetch.mock.calls[0]?.[0]).toBe(
      "/api/sysdictdata/by-code/Unit%20%2F%20%E4%B8%AD%E6%96%87",
    );
    expect(fetch.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
    await getProductWarehouseCandidates({ Keyword: "仓库 & A", PageIndex: 2 });
    const url = new URL(
      String(fetch.mock.calls[1]?.[0]),
      "https://example.invalid",
    );
    expect(url.searchParams.get("Keyword")).toBe("仓库 & A");
    expect(url.searchParams.get("PageIndex")).toBe("2");
  });

  it.each([400, 403, 422, 500, 503])(
    "HTTP %i 保留完整业务失败正文，不自动重试或判定未生效",
    async (status) => {
      const body = {
        success: false,
        data: null,
        error: { code: "BUSINESS_BLOCKED", message: "受控拒绝" },
      };
      const fetch = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(response(body, status));
      const result = await saveProductBatch({ updateItems: [] });
      expect(result.status).toBe(status);
      expect(result.data).toEqual(body);
      expect(getRequestFacts(result)?.rawBody).toBe(JSON.stringify(body));
      expect(result.headers.get("Retry-After")).toBe("5");
      expect(fetch).toHaveBeenCalledTimes(1);
    },
  );

  it("行级部分成功不被通用层抹掉或接纳", async () => {
    const body = {
      success: true,
      data: {
        allSucceeded: false,
        items: [
          { clientKey: "a", success: true },
          { clientKey: "b", success: false },
        ],
      },
    };
    vi.spyOn(globalThis, "fetch").mockResolvedValue(response(body));
    expect((await saveProductBatch({})).data).toEqual(body);
  });

  it("派发前已取消可以证明本次没有调用 fetch", async () => {
    const fetch = vi.spyOn(globalThis, "fetch");
    const controller = new AbortController();
    controller.abort();
    await expect(getMe({ signal: controller.signal })).rejects.toMatchObject({
      kind: "not-sent",
      facts: { phase: "not-sent" },
    });
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([
    new TypeError("network"),
    new DOMException("cancelled", "AbortError"),
  ])("派发后的网络或取消只保留未知结果事实", async (error) => {
    const fetch = vi.spyOn(globalThis, "fetch").mockRejectedValue(error);
    await expect(saveProductBatch({})).rejects.toMatchObject({
      facts: { phase: "dispatched" },
    });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("不可解析响应保留状态、追踪和原始正文", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("<html>gateway</html>", {
        status: 502,
        headers: { "X-Correlation-ID": "gateway" },
      }),
    );
    try {
      await getMe();
      expect.fail("应拒绝不可解析响应。");
    } catch (error) {
      expect(error).toBeInstanceOf(RequestTransportError);
      expect(error).toMatchObject({
        kind: "invalid-response",
        facts: {
          phase: "response",
          status: 502,
          rawBody: "<html>gateway</html>",
          correlationId: "gateway",
        },
      });
    }
  });

  it("认证事件不包含令牌，登录失败不影响其他会话", async () => {
    const events: unknown[] = [];
    const unsubscribe = subscribeAuthenticationEvents((event) =>
      events.push(event),
    );
    try {
      setAccessToken("private-token");
      vi.spyOn(globalThis, "fetch").mockImplementation(async () =>
        response({ success: false }, 401),
      );
      await login({ userName: "x", password: "x" });
      expect(events).toHaveLength(0);
      await getMe();
      expect(events).toHaveLength(1);
      expect(JSON.stringify(events)).not.toContain("private-token");
    } finally {
      unsubscribe();
    }
  });
});
