import { afterEach, describe, expect, it, vi } from "vitest";
import type { MockInstance } from "vitest";
import { getDictionaryByCode } from "../api/generated/client";
import { setAccessToken } from "../api/请求执行";
import { createQueryClient } from "../query/查询客户端";
import { queryKeys } from "../query/查询键";
import { createSessionStore } from "./会话状态";

const cleanups: (() => void)[] = [];
type FetchMock = MockInstance<typeof globalThis.fetch>;

/** 仅构造测试 JWT 形状和 exp；固定签名占位不代表服务器验签或真实凭据。 */
function jwt(
  expiresAt = Date.now() + 12 * 60 * 60 * 1000,
  marker = "a",
): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value))
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "");
  return `${encode({ alg: "HS256", typ: "JWT" })}.${encode({ exp: Math.floor(expiresAt / 1000), marker })}.test-signature`;
}

/** 模拟 HTTP 返回，保留生成 Fetch 的状态及完整正文处理。 */
function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

/** 稳定身份及权限来自模拟 me，而不是测试直接修改客户端状态。 */
function identity(
  userId = "user-a",
  permissions: unknown = ["Products.Product.View"],
): Response {
  return response({
    userId,
    userName: userId.replace("user-", ""),
    permissions,
  });
}

/** 每个测试释放订阅、计时器及 Query，任何未安排请求都应使测试失败。 */
function fixture() {
  const fetch = vi
    .spyOn(globalThis, "fetch")
    .mockRejectedValue(new Error("出现未安排的模拟请求。"));
  const queryClient = createQueryClient();
  const store = createSessionStore(queryClient);
  const stop = store.start();
  let stopped = false;
  const release = () => {
    if (!stopped) {
      stopped = true;
      stop();
    }
  };
  cleanups.push(() => {
    release();
    queryClient.clear();
  });
  return { fetch, queryClient, store, stop: release };
}

/** 登录依次经过 csrf、login、Cookie restore 确认和 me，均由生成客户端发起。 */
function queueLogin(fetch: FetchMock, token: string, me = identity()): void {
  fetch
    .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
    .mockResolvedValueOnce(response({ access_token: token }))
    .mockResolvedValueOnce(response({ access_token: token }))
    .mockResolvedValueOnce(me);
}

/** 只有204及随后restore=401才确认退出，不模拟服务器撤销JWT。 */
function queueLogout(fetch: FetchMock): void {
  fetch
    .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
    .mockResolvedValueOnce(new Response(null, { status: 204 }))
    .mockResolvedValueOnce(response({}, 401));
}

/** 故意忽略AbortSignal的延迟响应验证迟到结果，不能只依赖网络取消。 */
function deferredResponse() {
  let resolve!: (value: Response) => void;
  const promise = new Promise<Response>((accept) => {
    resolve = accept;
  });
  return { promise, resolve };
}

afterEach(() => {
  for (const cleanup of cleanups.splice(0)) cleanup();
  setAccessToken(null);
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("浏览器会话、权限及身份隔离", () => {
  it("登录确认Cookie再读me，401保留原身份；核权变化只更新权限编号并清旧快照", async () => {
    const { store, fetch, queryClient } = fixture();
    const token = jwt();
    queueLogin(fetch, token);
    await store.login({ userName: "a", password: "test-password" });
    expect(fetch.mock.calls.map(([path]) => path)).toEqual([
      "/api/auth/browser-session/csrf",
      "/api/auth/login",
      "/api/auth/browser-session/restore",
      "/api/auth/me",
    ]);
    expect(fetch.mock.calls).toHaveLength(4);
    for (const index of [0, 1, 2]) {
      expect(fetch.mock.calls[index]![1]?.credentials).toBe("same-origin");
      expect(
        new Headers(fetch.mock.calls[index]![1]?.headers).has("Authorization"),
      ).toBe(false);
    }
    expect(
      new Headers(fetch.mock.calls[1]![1]?.headers).get("X-CSRF-TOKEN"),
    ).toBe("test-csrf");
    expect(
      new Headers(fetch.mock.calls[1]![1]?.headers).get(
        "X-SwiftExport-Web-Session",
      ),
    ).toBe("1");
    expect(fetch.mock.calls[3]![1]?.credentials).toBe("omit");
    expect(
      new Headers(fetch.mock.calls[3]![1]?.headers).get("Authorization"),
    ).toBe(`Bearer ${token}`);
    const initial = store.getSnapshot();
    const oldKey = queryKeys.operation(
      {
        userId: "user-a",
        identityEpoch: initial.identityEpoch,
        permissionEpoch: initial.permissionEpoch,
      },
      "probe.read",
    );
    queryClient.setQueryData(oldKey, { retained: true });
    fetch.mockResolvedValueOnce(response({}, 401));
    await getDictionaryByCode("Unit");
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
      identityEpoch: initial.identityEpoch,
    });
    expect(queryClient.getQueryData(oldKey)).toEqual({ retained: true });
    queueLogin(
      fetch,
      jwt(undefined, "a-new"),
      identity("user-a", ["Products.Product.Edit"]),
    );
    await store.login({ userName: "a", password: "test-password" });
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { permissions: ["Products.Product.Edit"] },
      identityEpoch: initial.identityEpoch,
      permissionEpoch: initial.permissionEpoch + 1,
    });
    expect(queryClient.getQueryData(oldKey)).toBeUndefined();
  });

  it("同用户恢复原JWT且权限未变时保持草稿身份和既有快照", async () => {
    const { store, fetch, queryClient } = fixture();
    const token = jwt();
    queueLogin(fetch, token);
    await store.login({ userName: "a", password: "test-password" });
    const initial = store.getSnapshot();
    const key = queryKeys.operation(
      {
        userId: "user-a",
        identityEpoch: initial.identityEpoch,
        permissionEpoch: initial.permissionEpoch,
      },
      "probe.read",
    );
    queryClient.setQueryData(key, "server-snapshot");
    fetch.mockResolvedValueOnce(response({}, 401));
    await getDictionaryByCode("Unit");
    fetch
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(identity());
    await store.restore();
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      identityEpoch: initial.identityEpoch,
      permissionEpoch: initial.permissionEpoch,
    });
    expect(queryClient.getQueryData(key)).toBe("server-snapshot");
  });

  it("切换用户先确认退出并清旧缓存，再接纳新身份", async () => {
    const { store, fetch, queryClient } = fixture();
    queueLogin(fetch, jwt());
    await store.login({ userName: "a", password: "test-password" });
    const initial = store.getSnapshot();
    const key = queryKeys.operation(
      {
        userId: "user-a",
        identityEpoch: initial.identityEpoch,
        permissionEpoch: initial.permissionEpoch,
      },
      "probe.read",
    );
    queryClient.setQueryData(key, { private: "a" });
    await expect(
      store.login({ userName: "b", password: "test-password" }),
    ).rejects.toThrow("先完成当前会话退出");
    expect(fetch).toHaveBeenCalledTimes(4);
    queueLogout(fetch);
    await store.logout();
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
      identityEpoch: initial.identityEpoch + 1,
      permissionEpoch: 0,
    });
    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
    queueLogin(fetch, jwt(undefined, "b"), identity("user-b"));
    await store.login({ userName: "b", password: "test-password" });
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { userId: "user-b" },
      identityEpoch: initial.identityEpoch + 2,
    });
    expect(queryClient.getQueryData(key)).toBeUndefined();
  });

  it("403触发重新核权，me自身403不递归，失败后仍保留原身份", async () => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt());
    await store.login({ userName: "a", password: "test-password" });
    fetch
      .mockResolvedValueOnce(response({}, 403))
      .mockResolvedValueOnce(response({}, 403));
    await getDictionaryByCode("Unit");
    await vi.waitFor(() =>
      expect(store.getSnapshot().status).toBe("reauthenticating"),
    );
    expect(store.getSnapshot().user?.userId).toBe("user-a");
    expect(fetch).toHaveBeenCalledTimes(6);
  });

  it("迟到旧Token的401不能冻结退出后登录的新用户", async () => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt());
    await store.login({ userName: "a", password: "test-password" });
    const late = deferredResponse();
    fetch.mockImplementationOnce(() => late.promise);
    const oldRead = getDictionaryByCode("Unit");
    queueLogout(fetch);
    await store.logout();
    queueLogin(fetch, jwt(undefined, "b"), identity("user-b"));
    await store.login({ userName: "b", password: "test-password" });
    late.resolve(response({}, 401));
    await oldRead;
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { userId: "user-b" },
    });
  });

  it("登录me晚于退出确认时不得重新接纳身份", async () => {
    const { store, fetch } = fixture();
    const token = jwt();
    const late = deferredResponse();
    fetch
      .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockImplementationOnce(() => late.promise);
    const login = store.login({ userName: "a", password: "test-password" });
    const rejected = expect(login).rejects.toThrow("会话已变化");
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
    queueLogout(fetch);
    await store.logout();
    late.resolve(identity());
    await rejected;
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
    });
  });

  it.each(["", "   "])("me缺少稳定Uid(%s)时拒绝开放工作区", async (uid) => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt(), identity(uid));
    await expect(
      store.login({ userName: "a", password: "test-password" }),
    ).rejects.toThrow("稳定用户标识");
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
    });
  });

  it.each([
    { label: "非数组", permissions: true },
    { label: "数组中含非字符串", permissions: ["Products.Product.View", 123] },
  ])("非法权限形态$label不能开放工作区", async ({ permissions }) => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt(), identity("user-a", permissions));
    await expect(
      store.login({ userName: "a", password: "test-password" }),
    ).rejects.toThrow("权限响应格式无法核实");
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
    });
  });

  it("同用户重认证正在核实时拒绝重复登录，非法权限不回写原身份", async () => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt());
    await store.login({ userName: "a", password: "test-password" });
    const epoch = store.getSnapshot().identityEpoch;
    fetch.mockResolvedValueOnce(response({}, 401));
    await getDictionaryByCode("Unit");
    const pending = deferredResponse();
    const token = jwt(undefined, "a-new");
    fetch
      .mockImplementationOnce(() => pending.promise)
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(identity("user-a", true));
    const login = store.login({ userName: "a", password: "test-password" });
    const failed = expect(login).rejects.toThrow("权限响应格式无法核实");
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
      identityEpoch: epoch,
    });
    await expect(
      store.login({ userName: "a", password: "test-password" }),
    ).rejects.toThrow("登录正在进行");
    pending.resolve(response({ requestToken: "test-csrf" }));
    await failed;
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a", permissions: ["Products.Product.View"] },
      identityEpoch: epoch,
    });
  });

  it("相同恢复动作复用Promise和请求，Cookie恢复不携带旧Bearer", async () => {
    const { store, fetch } = fixture();
    const pending = deferredResponse();
    fetch
      .mockImplementationOnce(() => pending.promise)
      .mockResolvedValueOnce(identity());
    const first = store.restore();
    const second = store.restore();
    expect(first).toBe(second);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls).toHaveLength(1);
    expect(fetch.mock.calls[0]![1]?.credentials).toBe("same-origin");
    expect(
      new Headers(fetch.mock.calls[0]![1]?.headers).has("Authorization"),
    ).toBe(false);
    pending.resolve(response({ access_token: jwt() }));
    await Promise.all([first, second]);
    expect(store.getSnapshot().status).toBe("authenticated");
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("首次恢复网络失败提供失败状态，再次主动恢复可成功", async () => {
    const { store, fetch } = fixture();
    fetch.mockRejectedValueOnce(new TypeError("模拟网络故障"));
    await expect(store.restore()).rejects.toMatchObject({ kind: "network" });
    expect(store.getSnapshot()).toMatchObject({
      status: "restore-failed",
      user: null,
    });
    expect(store.getSnapshot().failure).toContain("可重试");
    fetch
      .mockResolvedValueOnce(response({ access_token: jwt() }))
      .mockResolvedValueOnce(identity());
    await store.restore();
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { userId: "user-a" },
      failure: null,
    });
  });

  it("恢复401进入匿名；密码登录没有确认Cookie时不能继续读me", async () => {
    const { store, fetch } = fixture();
    fetch.mockResolvedValueOnce(response({}, 401));
    await store.restore();
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
    });
    fetch
      .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
      .mockResolvedValueOnce(response({ access_token: jwt() }))
      .mockResolvedValueOnce(response({}, 401));
    await expect(
      store.login({ userName: "a", password: "test-password" }),
    ).rejects.toThrow("未确认保存本次登录");
    expect(
      fetch.mock.calls.filter(([path]) => path === "/api/auth/me"),
    ).toHaveLength(0);
  });

  it("过exp的恢复令牌不发送me，也不开放工作区", async () => {
    const { store, fetch } = fixture();
    fetch.mockResolvedValueOnce(
      response({ access_token: jwt(Date.now() - 1000) }),
    );
    await expect(store.restore()).rejects.toThrow("已到期");
    expect(store.getSnapshot()).toMatchObject({
      status: "restore-failed",
      user: null,
    });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("原exp到期锁界面但保留身份和快照，不自动重发或续签", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-09T04:00:00Z"));
    const { store, fetch, queryClient } = fixture();
    queueLogin(fetch, jwt(Date.now() + 2000));
    await store.login({ userName: "a", password: "test-password" });
    const state = store.getSnapshot();
    const key = queryKeys.operation(
      {
        userId: "user-a",
        identityEpoch: state.identityEpoch,
        permissionEpoch: state.permissionEpoch,
      },
      "probe.read",
    );
    queryClient.setQueryData(key, "baseline-snapshot");
    await vi.advanceTimersByTimeAsync(2000);
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
      identityEpoch: state.identityEpoch,
    });
    expect(queryClient.getQueryData(key)).toBe("baseline-snapshot");
    expect(fetch).toHaveBeenCalledTimes(4);
  });

  it("me拖过原exp时登录拒绝，不能短暂发布authenticated", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-09T04:00:00Z"));
    const { store, fetch } = fixture();
    const token = jwt(Date.now() + 2000);
    const pending = deferredResponse();
    let markStarted!: () => void;
    const started = new Promise<void>((resolve) => {
      markStarted = resolve;
    });
    const states: string[] = [];
    const stop = store.subscribe(() => states.push(store.getSnapshot().status));
    fetch
      .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockImplementationOnce(() => {
        markStarted();
        return pending.promise;
      });
    const login = store.login({ userName: "a", password: "test-password" });
    const failed = expect(login).rejects.toThrow("已到期");
    await started;
    await vi.advanceTimersByTimeAsync(2000);
    pending.resolve(identity());
    await failed;
    expect(states).not.toContain("authenticated");
    expect(store.getSnapshot().user).toBeNull();
    stop();
  });

  it.each(["login", "restore"] as const)(
    "%s核实me后发布身份期间跨过exp，重认证不遗留busy锁或旧恢复Promise",
    async (mode) => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-10-09T04:00:00Z"));
      const { store, fetch } = fixture();
      const expiry = Date.now() + 1000;
      const token = jwt(expiry);
      let crossedExpiry = false;
      cleanups.push(
        store.subscribe(() => {
          if (
            !crossedExpiry &&
            store.getSnapshot().status === "authenticated"
          ) {
            crossedExpiry = true;
            // 仅移动时钟，不派发定时器：精确覆盖发布摘要后、armExpiry同步复核前的间隔。
            vi.setSystemTime(expiry);
          }
        }),
      );
      if (mode === "login") queueLogin(fetch, token);
      else
        fetch
          .mockResolvedValueOnce(response({ access_token: token }))
          .mockResolvedValueOnce(identity());
      const first =
        mode === "login"
          ? store.login({ userName: "a", password: "test-password" })
          : store.restore();
      await first;
      expect(crossedExpiry).toBe(true);
      expect(store.getSnapshot()).toMatchObject({
        status: "reauthenticating",
        user: { userId: "user-a" },
      });
      const identityEpoch = store.getSnapshot().identityEpoch;
      const previousCalls = fetch.mock.calls.length;

      // 过期Cookie由服务器拒绝；主动恢复必须发起新动作，不能复用已经废弃的Promise。
      fetch.mockResolvedValueOnce(response({}, 401));
      const retry = store.restore();
      expect(retry).not.toBe(first);
      await retry;
      expect(fetch).toHaveBeenCalledTimes(previousCalls + 1);
      expect(store.getSnapshot().status).toBe("reauthenticating");

      queueLogin(fetch, jwt(undefined, "same-user-reauthenticated"));
      await store.login({ userName: "a", password: "test-password" });
      expect(store.getSnapshot()).toMatchObject({
        status: "authenticated",
        user: { userId: "user-a" },
        identityEpoch,
      });
    },
  );

  it("释放最后生命周期订阅后停止到期计时器", async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-09T04:00:00Z"));
    const { store, fetch, stop } = fixture();
    queueLogin(fetch, jwt(Date.now() + 2000));
    await store.login({ userName: "a", password: "test-password" });
    stop();
    await vi.advanceTimersByTimeAsync(2000);
    expect(store.getSnapshot().status).toBe("authenticated");
    const again = store.start();
    expect(store.getSnapshot().status).toBe("reauthenticating");
    again();
  });

  it("退出未取得204保留未确认状态，禁止换用户，允许主动重试", async () => {
    const { store, fetch } = fixture();
    queueLogin(fetch, jwt());
    await store.login({ userName: "a", password: "test-password" });
    fetch
      .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
      .mockResolvedValueOnce(response({}, 503));
    await expect(store.logout()).rejects.toThrow("退出未确认");
    expect(store.getSnapshot()).toMatchObject({
      status: "logout-unconfirmed",
      user: { userId: "user-a" },
    });
    await expect(
      store.login({ userName: "b", password: "test-password" }),
    ).rejects.toThrow("先完成当前会话退出");
    queueLogout(fetch);
    await store.logout();
    expect(store.getSnapshot()).toMatchObject({
      status: "anonymous",
      user: null,
    });
  });

  it.each(["restorable", "http-error", "network"])(
    "204后检查%s不等于确认退出，也不得接纳身份",
    async (kind) => {
      const { store, fetch, queryClient } = fixture();
      queueLogin(fetch, jwt());
      await store.login({ userName: "a", password: "test-password" });
      fetch
        .mockResolvedValueOnce(response({ requestToken: "test-csrf" }))
        .mockResolvedValueOnce(new Response(null, { status: 204 }));
      if (kind === "network")
        fetch.mockRejectedValueOnce(new TypeError("模拟检查响应丢失"));
      else
        fetch.mockResolvedValueOnce(
          kind === "restorable"
            ? response({ access_token: jwt() })
            : response({}, 503),
        );
      await expect(store.logout()).rejects.toThrow();
      expect(store.getSnapshot()).toMatchObject({
        status: "logout-unconfirmed",
        user: null,
      });
      expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
      await expect(store.restore()).rejects.toThrow("尚未完成");
      await expect(
        store.login({ userName: "b", password: "test-password" }),
      ).rejects.toThrow("先完成当前会话退出");
      queueLogout(fetch);
      await store.logout();
      expect(store.getSnapshot().status).toBe("anonymous");
    },
  );

  it("旧恢复finally晚于退出和新登录时不得释放新登录busy锁", async () => {
    const { store, fetch } = fixture();
    const oldResponse = deferredResponse();
    fetch.mockImplementationOnce(() => oldResponse.promise);
    const oldRestore = store.restore();
    const oldRejected = expect(oldRestore).rejects.toThrow("会话已变化");
    queueLogout(fetch);
    await store.logout();
    const nextCsrf = deferredResponse();
    const token = jwt(undefined, "new-login");
    fetch
      .mockImplementationOnce(() => nextCsrf.promise)
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(response({ access_token: token }))
      .mockResolvedValueOnce(identity());
    const newLogin = store.login({ userName: "a", password: "test-password" });
    oldResponse.resolve(
      response({ access_token: jwt(undefined, "old-restore") }),
    );
    await oldRejected;
    await expect(
      store.login({ userName: "a", password: "test-password" }),
    ).rejects.toThrow("登录正在进行");
    await expect(store.restore()).rejects.toThrow("尚未完成");
    nextCsrf.resolve(response({ requestToken: "test-csrf" }));
    await newLogin;
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { userId: "user-a" },
    });
  });

  it("统一查询键隔离身份权限，读写重试和窗口焦点刷新关闭", () => {
    const client = createQueryClient();
    const first = { userId: "a", identityEpoch: 1, permissionEpoch: 0 };
    expect(queryKeys.operation(first, "read", { id: "1" })).not.toEqual(
      queryKeys.operation(
        { userId: "b", identityEpoch: 2, permissionEpoch: 0 },
        "read",
        { id: "1" },
      ),
    );
    expect(queryKeys.operation(first, "read")).not.toEqual(
      queryKeys.operation({ ...first, permissionEpoch: 1 }, "read"),
    );
    expect(client.getDefaultOptions()).toMatchObject({
      queries: { retry: false, refetchOnWindowFocus: false },
      mutations: { retry: false },
    });
    client.clear();
  });
});
