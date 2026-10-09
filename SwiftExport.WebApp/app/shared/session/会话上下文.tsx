import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useSyncExternalStore,
} from "react";
import type { ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { createSessionStore } from "./会话状态";
import type { SessionActions, SessionState, SessionStore } from "./会话状态";

const SessionContext = createContext<SessionStore | null>(null);

/** 在浏览器交互阶段接线内存认证事件；初次渲染不读身份、不调用业务 API。 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const storeRef = useRef<SessionStore | null>(null);
  if (!storeRef.current) storeRef.current = createSessionStore(queryClient);
  const store = storeRef.current;
  useEffect(() => store.start(), [store]);
  return (
    <SessionContext.Provider value={store}>{children}</SessionContext.Provider>
  );
}

export { SessionProvider as 会话Provider };

/** 获取最新身份/权限及登录动作；同用户重新认证保持 identityEpoch 与草稿归属。 */
export function useSession(): SessionState & SessionActions {
  const store = useContext(SessionContext);
  if (!store) throw new Error("useSession 必须在会话Provider 内使用。");
  const state = useSyncExternalStore(
    store.subscribe,
    store.getSnapshot,
    store.getSnapshot,
  );
  return {
    ...state,
    login: store.login,
    logout: store.logout,
    refreshPermissions: store.refreshPermissions,
  };
}
