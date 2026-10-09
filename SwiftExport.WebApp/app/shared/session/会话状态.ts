import type { QueryClient } from "@tanstack/react-query";
import {
  getBrowserSessionCsrf,
  restoreBrowserSession,
  logoutBrowserSession,
  getMe,
  login as requestLogin,
} from "../api/generated/client";
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
import { clearDiagnostics, recordDiagnostic } from "../diagnostics/诊断记录";
import { readJwtExpiresAt } from "./JWT期限";

/** 会话只持有已核实身份、权限及恢复状态；草稿仍属于原 feature 会话。 */
export interface SessionState {
  readonly status:
    | "restoring"
    | "restore-failed"
    | "anonymous"
    | "authenticated"
    | "reauthenticating"
    | "signing-out"
    | "logout-unconfirmed";
  readonly user: MeDto | null;
  readonly identityEpoch: number;
  readonly permissionEpoch: number;
  readonly failure: string | null;
}
/** 会话动作不保存草稿；离开保护由 Workspace 在调用退出之前完成。 */
export interface SessionActions {
  readonly login: (request: LoginRequestDto) => Promise<void>;
  readonly restore: () => Promise<void>;
  readonly logout: () => Promise<void>;
  readonly refreshPermissions: () => Promise<void>;
}
/** 稳定订阅和生命周期入口，初次渲染不读取 Cookie 或发请求。 */
export interface SessionStore extends SessionActions {
  readonly getSnapshot: () => SessionState;
  readonly subscribe: (listener: () => void) => () => void;
  readonly start: () => () => void;
}
/** 创建原 JWT 恢复会话，按身份及权限版本隔离读取，不续签令牌。 */
export function createSessionStore(queryClient: QueryClient): SessionStore {
  let state: SessionState = {
    status: "restoring",
    user: null,
    identityEpoch: 0,
    permissionEpoch: 0,
    failure: null,
  };
  let generation = 0;
  let authBusy = false;
  let controller: AbortController | null = null;
  let restoring: Promise<void> | null = null;
  let refreshing: Promise<void> | null = null;
  let loggingOut: Promise<void> | null = null;
  let expiresAt: number | null = null;
  let expiryTimer: ReturnType<typeof setTimeout> | null = null;
  let starts = 0;
  const subscribers = new Set<() => void>();
  /** 发布完整摘要，导航及驻留页同时重新核权。 */
  function publish(next: SessionState): void {
    state = next;
    for (const subscriber of subscribers) subscriber();
  }
  /** 只取消读取及身份请求，不中断已派发的业务保存。 */
  function stopOldReads(): void {
    controller?.abort();
    controller = null;
    void queryClient.cancelQueries();
  }
  /** 定时器只锁界面；服务器仍是 JWT 有效性的权威。 */
  function stopTimer(): void {
    if (expiryTimer) clearTimeout(expiryTimer);
    expiryTimer = null;
  }
  /** 到期或认证失效时废弃旧认证动作并释放其锁，进入重认证但保留当前身份和编辑会话。 */
  function requireAuthentication(): void {
    generation += 1;
    stopOldReads();
    stopTimer();
    setAccessToken(null);
    // 身份发布后也可能同步跨过exp；旧generation的finally不再负责清理由此废弃的动作。
    authBusy = false;
    restoring = null;
    refreshing = null;
    publish({
      ...state,
      status: state.user ? "reauthenticating" : "anonymous",
      failure: null,
    });
  }
  /** 长期限分段计时并复核时钟；页面恢复焦点也使用同一检查。 */
  function armExpiry(): void {
    stopTimer();
    if (!starts || state.status !== "authenticated" || expiresAt === null)
      return;
    const remaining = expiresAt - Date.now();
    if (remaining <= 0) requireAuthentication();
    else
      expiryTimer = setTimeout(armExpiry, Math.min(remaining, 2_147_483_647));
  }
  /** 认证请求晚于退出或其他认证动作完成时不得回写。 */
  function assertCurrent(expected: number): void {
    if (generation !== expected)
      throw new Error("会话已变化，本次身份结果不再接纳。");
  }
  /** 相同用户权限变化只清旧业务快照，不递增草稿身份编号。 */
  function acceptIdentity(user: MeDto): void {
    if (!user || typeof user.userId !== "string" || !user.userId.trim())
      throw new Error("身份响应缺少稳定用户标识，不能开放工作区。");
    if (
      user.permissions != null &&
      (!Array.isArray(user.permissions) ||
        user.permissions.some((key) => typeof key !== "string"))
    )
      throw new Error("权限响应格式无法核实，不能开放工作区。");
    if (state.user && state.user.userId !== user.userId)
      throw new Error("身份与原会话不一致，请先处理原页签并退出后切换用户。");
    const sameIdentity = state.user?.userId === user.userId;
    const permissionsChanged =
      sameIdentity &&
      JSON.stringify([...new Set(state.user?.permissions ?? [])].sort()) !==
        JSON.stringify([...new Set(user.permissions ?? [])].sort());
    const identityEpoch = sameIdentity
      ? state.identityEpoch
      : state.identityEpoch + 1;
    const permissionEpoch = sameIdentity
      ? state.permissionEpoch + Number(permissionsChanged)
      : 0;
    if (!sameIdentity) {
      void queryClient.cancelQueries();
      queryClient.clear();
      clearDiagnostics();
    } else if (permissionsChanged) {
      const oldEpoch = state.permissionEpoch;
      const stale = {
        predicate: (query: { queryKey: readonly unknown[] }) =>
          query.queryKey[0] === "identity" &&
          query.queryKey[1] === state.identityEpoch &&
          query.queryKey[2] === user.userId &&
          query.queryKey[3] === oldEpoch,
      };
      void queryClient.cancelQueries(stale);
      queryClient.removeQueries(stale);
      recordDiagnostic({ event: "permission-changed", level: "info" });
    }
    queryClient.setQueryData(
      queryKeys.me({ userId: user.userId, identityEpoch, permissionEpoch }),
      user,
    );
    publish({
      status: "authenticated",
      user,
      identityEpoch,
      permissionEpoch,
      failure: null,
    });
    armExpiry();
  }
  /** 核对 JWT 到期及 me 后才开放工作区，不将解析 JWT 当身份验证。 */
  async function verifyToken(
    token: string,
    expected: number,
    signal: AbortSignal,
  ): Promise<void> {
    expiresAt = readJwtExpiresAt(token);
    if (expiresAt <= Date.now())
      throw new Error("原登录令牌已到期，请重新登录。");
    setAccessToken(token);
    const identity = await getMe({ signal });
    assertCurrent(expected);
    if (identity.status !== 200)
      throw new ApiResponseError(identity, "当前身份核实失败。");
    if (expiresAt <= Date.now())
      throw new Error("原登录令牌已到期，请重新登录。");
    acceptIdentity(identity.data);
  }
  /** csrf 始终匿名取回，令牌只用于本次同源会话动作。 */
  async function csrfHeaders(signal: AbortSignal): Promise<HeadersInit> {
    const response = await getBrowserSessionCsrf({ signal });
    if (response.status !== 200 || !response.data.requestToken)
      throw new ApiResponseError(response, "无法准备浏览器会话，请重试。");
    return { "X-CSRF-TOKEN": response.data.requestToken };
  }
  const store: SessionStore = {
    getSnapshot: () => state,
    subscribe: (listener) => {
      subscribers.add(listener);
      return () => subscribers.delete(listener);
    },
    start: () => {
      starts += 1;
      const stop = subscribeAuthenticationEvents((event) => {
        if (
          event.credentialRevision !== getCredentialRevision() ||
          authBusy ||
          !state.user ||
          state.status !== "authenticated"
        )
          return;
        if (event.status === 401) requireAuthentication();
        else if (event.path !== "/api/auth/me")
          void store.refreshPermissions().catch(() => undefined);
      });
      const focus = () => armExpiry();
      if (typeof window !== "undefined")
        window.addEventListener("focus", focus);
      armExpiry();
      return () => {
        stop();
        starts -= 1;
        if (!starts) stopTimer();
        if (typeof window !== "undefined")
          window.removeEventListener("focus", focus);
      };
    },
    restore: () => {
      if (restoring) return restoring;
      if (state.status === "authenticated") return Promise.resolve();
      if (
        authBusy ||
        state.status === "signing-out" ||
        state.status === "logout-unconfirmed"
      )
        return Promise.reject(new Error("当前会话动作尚未完成。"));
      authBusy = true;
      const expected = ++generation;
      stopOldReads();
      stopTimer();
      publish({
        ...state,
        status: state.user ? "reauthenticating" : "restoring",
        failure: null,
      });
      controller = new AbortController();
      const signal = controller.signal;
      restoring = (async () => {
        try {
          const response = await restoreBrowserSession({ signal });
          assertCurrent(expected);
          if (response.status === 401) {
            setAccessToken(null);
            publish({
              ...state,
              status: state.user ? "reauthenticating" : "anonymous",
              failure: null,
            });
            return;
          }
          if (response.status !== 200 || !response.data.access_token)
            throw new ApiResponseError(response, "登录恢复失败，请重试。");
          await verifyToken(response.data.access_token, expected, signal);
        } catch (error) {
          if (generation === expected) {
            setAccessToken(null);
            publish({
              ...state,
              status: state.user ? "reauthenticating" : "restore-failed",
              failure: "登录恢复失败，可重试或使用密码登录。",
            });
            recordDiagnostic({
              event: "session-restore-failed",
              level: "warn",
            });
          }
          throw error;
        } finally {
          if (generation === expected) {
            authBusy = false;
            restoring = null;
            controller = null;
          }
        }
      })();
      return restoring;
    },
    login: async (request) => {
      if (authBusy) throw new Error("登录正在进行，请等待本次身份核实。");
      if (
        state.status === "authenticated" ||
        state.status === "logout-unconfirmed" ||
        state.status === "signing-out"
      )
        throw new Error("切换用户须先完成当前会话退出。");
      if (
        state.user?.userName &&
        request.userName?.toLocaleLowerCase() !==
          state.user.userName.toLocaleLowerCase()
      )
        throw new Error("重新认证请使用原用户；切换用户须先处理原页签并退出。");
      authBusy = true;
      const expected = ++generation;
      stopOldReads();
      stopTimer();
      if (state.user)
        publish({ ...state, status: "reauthenticating", failure: null });
      controller = new AbortController();
      const signal = controller.signal;
      try {
        const headers = new Headers(await csrfHeaders(signal));
        headers.set("X-SwiftExport-Web-Session", "1");
        const response = await requestLogin(request, { signal, headers });
        assertCurrent(expected);
        if (response.status !== 200 || !response.data.access_token)
          throw new ApiResponseError(response, "登录未成功。");
        // 确认浏览器接纳恢复 Cookie 后才开放页面，避免声称可持久恢复但实际未保存。
        const restored = await restoreBrowserSession({ signal });
        assertCurrent(expected);
        if (
          restored.status !== 200 ||
          restored.data.access_token !== response.data.access_token
        )
          throw new Error(
            "浏览器未确认保存本次登录，请检查 HTTPS 或 Cookie 设置后重试。",
          );
        await verifyToken(response.data.access_token, expected, signal);
      } catch (error) {
        if (generation === expected) {
          setAccessToken(null);
          publish({
            ...state,
            status: state.user ? "reauthenticating" : "anonymous",
            failure: null,
          });
        }
        throw error;
      } finally {
        if (generation === expected) {
          authBusy = false;
          controller = null;
        }
      }
    },
    logout: () => {
      if (loggingOut) return loggingOut;
      const expected = ++generation;
      stopOldReads();
      restoring = null;
      refreshing = null;
      stopTimer();
      authBusy = true;
      publish({ ...state, status: "signing-out", failure: null });
      controller = new AbortController();
      const signal = controller.signal;
      loggingOut = (async () => {
        try {
          const response = await logoutBrowserSession({
            signal,
            headers: await csrfHeaders(signal),
          });
          assertCurrent(expected);
          if (response.status !== 204)
            throw new ApiResponseError(response, "退出未确认，请重试。");
          setAccessToken(null);
          expiresAt = null;
          queryClient.clear();
          clearDiagnostics();
          publish({
            ...state,
            user: null,
            identityEpoch: state.identityEpoch + 1,
            permissionEpoch: 0,
          });
          const check = await restoreBrowserSession({ signal });
          assertCurrent(expected);
          if (check.status !== 401)
            throw new Error("退出后的恢复凭据尚未确认清除，请重试。");
          publish({ ...state, status: "anonymous", failure: null });
        } catch (error) {
          if (generation === expected) {
            setAccessToken(null);
            publish({
              ...state,
              status: "logout-unconfirmed",
              failure: "退出尚未确认，当前页面已停止业务操作，请重试退出。",
            });
          }
          throw error;
        } finally {
          if (generation === expected) {
            authBusy = false;
            loggingOut = null;
            controller = null;
          }
        }
      })();
      return loggingOut;
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
          const response = await getMe({ signal });
          assertCurrent(expected);
          if (getCredentialRevision() !== revision)
            throw new Error("认证凭据已变化，本次权限结果不再接纳。");
          if (response.status !== 200)
            throw new ApiResponseError(response, "权限核实失败。");
          acceptIdentity(response.data);
        } catch (error) {
          if (generation === expected) requireAuthentication();
          throw error;
        } finally {
          if (generation === expected) {
            refreshing = null;
            controller = null;
          }
        }
      })();
      return refreshing;
    },
  };
  return store;
}
