import type { QueryClient } from "@tanstack/react-query";
import { getMe, login as requestLogin } from "../api/generated/client";
import type {
  SwiftExportContractsDTOsAuthLoginRequestDto as LoginRequestDto,
  SwiftExportContractsDTOsAuthMeDto as MeDto,
} from "../api/generated/client";
import { ApiResponseError } from "../api/响应错误";
import {
  getCredentialRevision,
  setAccessToken,
  subscribeAuthenticationEvents,
} from "../api/请求执行";
import { queryKeys } from "../query/查询键";

/** 会话状态只持有身份和权限快照；401 后保留身份，工作区由宿主遮蔽。 */
export interface SessionState {
  readonly status: "anonymous" | "authenticated" | "reauthenticating";
  readonly user: MeDto | null;
  readonly identityEpoch: number;
}

/** 登录由 auth feature 发起，缓存隔离与重新核权由会话层负责。 */
export interface SessionActions {
  readonly login: (request: LoginRequestDto) => Promise<void>;
  readonly logout: () => void;
  readonly refreshPermissions: () => Promise<void>;
}

/** 同一会话的稳定订阅契约；不保存表单或任何业务草稿。 */
export interface SessionStore extends SessionActions {
  readonly getSnapshot: () => SessionState;
  readonly subscribe: (listener: () => void) => () => void;
  readonly start: () => () => void;
}

/** 创建内存会话，按稳定 userId 区分同用户恢复与身份替换。 */
export function createSessionStore(queryClient: QueryClient): SessionStore {
  let state: SessionState = {
    status: "anonymous",
    user: null,
    identityEpoch: 0,
  };
  let generation = 0;
  let authBusy = false;
  let controller: AbortController | null = null;
  let refreshing: Promise<void> | null = null;
  const subscribers = new Set<() => void>();

  /** 发布新身份快照，让导航、已打开页及权限入口同时重新求值。 */
  function publish(next: SessionState): void {
    state = next;
    for (const subscriber of subscribers) subscriber();
  }

  /** 仅取消旧读取与认证请求；不会把取消解释为后台写入回滚。 */
  function stopOldReads(): void {
    controller?.abort();
    controller = null;
    void queryClient.cancelQueries();
  }

  /** 核实完整身份后才开放内容；身份替换同时清除旧缓存并递增会话编号。 */
  function acceptIdentity(user: MeDto): void {
    if (!user || typeof user.userId !== "string" || !user.userId.trim())
      throw new Error("身份响应缺少稳定用户标识，不能开放工作区。");
    if (
      user.permissions != null &&
      (!Array.isArray(user.permissions) ||
        user.permissions.some((permission) => typeof permission !== "string"))
    )
      throw new Error("权限响应格式无法核实，不能开放工作区。");
    const sameIdentity = state.user?.userId === user.userId;
    const identityEpoch = sameIdentity
      ? state.identityEpoch
      : state.identityEpoch + 1;
    if (!sameIdentity) {
      void queryClient.cancelQueries();
      queryClient.clear();
    }
    queryClient.setQueryData(
      queryKeys.me({ userId: user.userId, identityEpoch }),
      user,
    );
    publish({ status: "authenticated", user, identityEpoch });
  }

  /** 过期请求不得写回换用户后的身份；中断是客户端事实，不是提交结果。 */
  function assertCurrent(expected: number): void {
    if (generation !== expected)
      throw new Error("会话已变化，本次身份结果不再接纳。");
  }

  const store: SessionStore = {
    getSnapshot: () => state,
    subscribe: (listener) => {
      subscribers.add(listener);
      return () => subscribers.delete(listener);
    },
    start: () =>
      subscribeAuthenticationEvents((event) => {
        if (
          event.credentialRevision !== getCredentialRevision() ||
          authBusy ||
          !state.user
        )
          return;
        if (event.status === 401) {
          generation += 1;
          stopOldReads();
          setAccessToken(null);
          publish({ ...state, status: "reauthenticating" });
        } else if (
          event.path !== "/api/auth/me" &&
          state.status === "authenticated"
        ) {
          // me 的 403 不递归触发自身；失败后遮蔽内容，直到重新认证核权。
          void store.refreshPermissions().catch(() => undefined);
        }
      }),
    login: async (request) => {
      if (authBusy) throw new Error("登录正在进行，请等待本次身份核实。");
      authBusy = true;
      generation += 1;
      const expected = generation;
      stopOldReads();
      if (state.user) publish({ ...state, status: "reauthenticating" });
      controller = new AbortController();
      try {
        const tokenResponse = await requestLogin(request, {
          signal: controller.signal,
        });
        assertCurrent(expected);
        if (
          tokenResponse.status !== 200 ||
          typeof tokenResponse.data?.access_token !== "string" ||
          !tokenResponse.data.access_token.trim()
        )
          throw new ApiResponseError(tokenResponse, "登录未成功。");
        setAccessToken(tokenResponse.data.access_token);
        const identity = await getMe({ signal: controller.signal });
        assertCurrent(expected);
        if (identity.status !== 200)
          throw new ApiResponseError(identity, "当前身份核实失败。");
        acceptIdentity(identity.data);
      } catch (error) {
        if (generation === expected) {
          setAccessToken(null);
          publish({
            ...state,
            status: state.user ? "reauthenticating" : "anonymous",
          });
        }
        throw error;
      } finally {
        authBusy = false;
        if (generation === expected) controller = null;
      }
    },
    logout: () => {
      generation += 1;
      stopOldReads();
      setAccessToken(null);
      queryClient.clear();
      publish({
        status: "anonymous",
        user: null,
        identityEpoch: state.identityEpoch + 1,
      });
    },
    refreshPermissions: () => {
      if (refreshing) return refreshing;
      if (state.status !== "authenticated" || !state.user || authBusy)
        return Promise.reject(new Error("当前会话不能重新读取权限。"));
      const expected = generation;
      const revision = getCredentialRevision();
      controller = new AbortController();
      const signal = controller.signal;
      refreshing = (async () => {
        try {
          const identity = await getMe({ signal });
          assertCurrent(expected);
          if (getCredentialRevision() !== revision)
            throw new Error("认证凭据已变化，本次权限结果不再接纳。");
          if (identity.status !== 200)
            throw new ApiResponseError(identity, "权限核实失败。");
          acceptIdentity(identity.data);
        } catch (error) {
          if (generation === expected) {
            setAccessToken(null);
            publish({ ...state, status: "reauthenticating" });
          }
          throw error;
        } finally {
          refreshing = null;
          if (generation === expected) controller = null;
        }
      })();
      return refreshing;
    },
  };
  return store;
}
