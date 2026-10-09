import { afterEach, describe, expect, it, vi } from "vitest";
import { getDictionaryByCode } from "../api/generated/client";
import { setAccessToken } from "../api/请求执行";
import { createQueryClient } from "../query/查询客户端";
import { queryKeys } from "../query/查询键";
import { createSessionStore } from "./会话状态";

const stops: (() => void)[] = [];

/** 用稳定用户标识和权限构造本次会话测试的模拟 me 响应。 */
function identity(
  userId: string,
  permissions = ["Products.Product.View"],
): Response {
  return new Response(
    JSON.stringify({ userId, userName: userId, permissions }),
    { status: 200 },
  );
}

/** 每次测试独立注册并释放认证订阅，防止模拟响应串扰。 */
function fixture() {
  const queryClient = createQueryClient();
  const store = createSessionStore(queryClient);
  stops.push(store.start());
  return { store, queryClient };
}

afterEach(() => {
  for (const stop of stops.splice(0)) stop();
  setAccessToken(null);
  vi.restoreAllMocks();
});

describe("内存会话与身份隔离", () => {
  it("登录必须读 me；401 保留用户，同用户重新认证保持会话编号并更新权限", async () => {
    const { store, queryClient } = fixture();
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockResolvedValueOnce(identity("user-a"))
      .mockResolvedValueOnce(new Response('{"success":false}', { status: 401 }))
      .mockResolvedValueOnce(new Response('{"access_token":"token-a-2"}'))
      .mockResolvedValueOnce(identity("user-a", ["Products.Product.Edit"]));
    await store.login({ userName: "a", password: "secret" });
    const epoch = store.getSnapshot().identityEpoch;
    queryClient.setQueryData(
      queryKeys.operation(
        { userId: "user-a", identityEpoch: epoch },
        "probe.read",
      ),
      { retained: true },
    );
    await getDictionaryByCode("Unit");
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
      identityEpoch: epoch,
    });
    await store.login({ userName: "a", password: "secret" });
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { permissions: ["Products.Product.Edit"] },
      identityEpoch: epoch,
    });
    expect(
      queryClient.getQueryData(
        queryKeys.operation(
          { userId: "user-a", identityEpoch: epoch },
          "probe.read",
        ),
      ),
    ).toEqual({ retained: true });
    expect(fetch).toHaveBeenCalledTimes(5);
  });

  it("换用户递增编号并清旧缓存，登出再次递增且清身份", async () => {
    const { store, queryClient } = fixture();
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockResolvedValueOnce(identity("user-a"))
      .mockResolvedValueOnce(new Response('{"access_token":"token-b"}'))
      .mockResolvedValueOnce(identity("user-b"));
    await store.login({ userName: "a", password: "x" });
    const first = store.getSnapshot().identityEpoch;
    const oldKey = queryKeys.operation(
      { userId: "user-a", identityEpoch: first },
      "probe.read",
    );
    queryClient.setQueryData(oldKey, { private: "a" });
    await store.login({ userName: "b", password: "x" });
    expect(store.getSnapshot()).toMatchObject({
      user: { userId: "user-b" },
      identityEpoch: first + 1,
    });
    expect(queryClient.getQueryData(oldKey)).toBeUndefined();
    store.logout();
    expect(store.getSnapshot()).toEqual({
      status: "anonymous",
      user: null,
      identityEpoch: first + 2,
    });
    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
  });

  it("403 重读权限，me 自身403不递归，无法核权时进入遮蔽状态", async () => {
    const { store } = fixture();
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token"}'))
      .mockResolvedValueOnce(identity("user-a"))
      .mockResolvedValueOnce(new Response('{"success":false}', { status: 403 }))
      .mockResolvedValueOnce(
        new Response('{"success":false}', { status: 403 }),
      );
    await store.login({ userName: "a", password: "x" });
    await getDictionaryByCode("Unit");
    await vi.waitFor(() =>
      expect(store.getSnapshot().status).toBe("reauthenticating"),
    );
    expect(fetch).toHaveBeenCalledTimes(4);
    expect(store.getSnapshot().user?.userId).toBe("user-a");
  });

  it("迟到的旧token 401不冻结新用户", async () => {
    const { store } = fixture();
    let resolveOld: ((response: Response) => void) | undefined;
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockResolvedValueOnce(identity("user-a"))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            resolveOld = resolve;
          }),
      )
      .mockResolvedValueOnce(new Response('{"access_token":"token-b"}'))
      .mockResolvedValueOnce(identity("user-b"));
    await store.login({ userName: "a", password: "x" });
    const oldRead = getDictionaryByCode("Unit");
    await store.login({ userName: "b", password: "x" });
    resolveOld?.(new Response('{"success":false}', { status: 401 }));
    await oldRead;
    expect(store.getSnapshot()).toMatchObject({
      status: "authenticated",
      user: { userId: "user-b" },
    });
    expect(fetch).toHaveBeenCalledTimes(5);
  });

  it("登出后迟到的 me 不得接纳身份", async () => {
    const { store } = fixture();
    let resolveMe: ((response: Response) => void) | undefined;
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            resolveMe = resolve;
          }),
      );
    const login = store.login({ userName: "a", password: "x" });
    await vi.waitFor(() => expect(resolveMe).toBeDefined());
    store.logout();
    resolveMe?.(identity("user-a"));
    await expect(login).rejects.toThrow("会话已变化");
    expect(store.getSnapshot().status).toBe("anonymous");
    expect(store.getSnapshot().user).toBeNull();
  });

  it("身份响应没有稳定uid时禁止开放工作区", async () => {
    const { store } = fixture();
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockResolvedValueOnce(identity(""));
    await expect(store.login({ userName: "a", password: "x" })).rejects.toThrow(
      "稳定用户标识",
    );
    expect(store.getSnapshot().status).toBe("anonymous");
  });

  it("错误权限形态不开放工作区，登录核实时不允许旧身份写入", async () => {
    const { store } = fixture();
    let resolveNew: ((response: Response) => void) | undefined;
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response('{"access_token":"token-a"}'))
      .mockResolvedValueOnce(identity("user-a"))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            resolveNew = resolve;
          }),
      )
      .mockResolvedValueOnce(
        new Response('{"userId":"user-b","permissions":true}'),
      );
    await store.login({ userName: "a", password: "x" });
    const nextLogin = store.login({ userName: "b", password: "x" });
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
    });
    await expect(store.login({ userName: "b", password: "x" })).rejects.toThrow(
      "登录正在进行",
    );
    resolveNew?.(new Response('{"access_token":"token-b"}'));
    await expect(nextLogin).rejects.toThrow("权限响应格式无法核实");
    expect(store.getSnapshot()).toMatchObject({
      status: "reauthenticating",
      user: { userId: "user-a" },
    });
  });

  it("统一查询键隔离身份，读写重试和窗口焦点刷新均关闭", () => {
    const client = createQueryClient();
    expect(
      queryKeys.operation({ userId: "a", identityEpoch: 1 }, "read", {
        id: "1",
      }),
    ).not.toEqual(
      queryKeys.operation({ userId: "b", identityEpoch: 2 }, "read", {
        id: "1",
      }),
    );
    expect(client.getDefaultOptions()).toMatchObject({
      queries: { retry: false, refetchOnWindowFocus: false },
      mutations: { retry: false },
    });
  });
});
