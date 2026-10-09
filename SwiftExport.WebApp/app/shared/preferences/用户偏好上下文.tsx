import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useSyncExternalStore,
} from "react";
import type { ReactNode } from "react";
import { createUserPreferenceStore } from "./用户偏好";
import type { UserPreferenceStore, UserPreferenceSnapshot } from "./用户偏好";

const UserPreferencesContext = createContext<UserPreferenceStore | null>(null);

/** 唯一偏好上下文；身份改变时先提供新默认值，浏览器 effect 才加载该用户的记录。 */
export function UserPreferencesProvider({
  userId,
  children,
}: {
  userId: string | null;
  children: ReactNode;
}) {
  const store = useMemo(() => createUserPreferenceStore(userId), [userId]);
  useEffect(() => store.load(), [store]);
  return (
    <UserPreferencesContext.Provider value={store}>
      {children}
    </UserPreferencesContext.Provider>
  );
}

/** 偏好调用方共享同一只读快照和类型明确动作，不自行创建第二份 Storage 状态。 */
export function useUserPreferences(): UserPreferenceSnapshot &
  Pick<
    UserPreferenceStore,
    "setSidebarCollapsed" | "setGridPreferences" | "resetPreferences"
  > {
  const store = useContext(UserPreferencesContext);
  if (!store)
    throw new Error(
      "useUserPreferences 必须在 UserPreferencesProvider 内使用。",
    );
  const snapshot = useSyncExternalStore(
    store.subscribe,
    store.getSnapshot,
    store.getSnapshot,
  );
  return {
    ...snapshot,
    setSidebarCollapsed: store.setSidebarCollapsed,
    setGridPreferences: store.setGridPreferences,
    resetPreferences: store.resetPreferences,
  };
}
