import type { TabDescriptor } from "../shared/workspace/页签契约";

/** 工作区只拥有页签目录、顺序和活动身份。 */
export interface WorkspaceState {
  tabs: readonly TabDescriptor[];
  activeId: string | null;
}
/** 业务当前值与回执不会进入工作区 reducer。 */
export type WorkspaceAction =
  | { type: "open"; tab: TabDescriptor }
  | { type: "activate"; id: string }
  | { type: "identity"; id: string; uid: string }
  | { type: "remove"; id: string };

/** 去重打开保留原实例，关闭活动页选择邻近页签。 */
export function workspaceReducer(
  state: WorkspaceState,
  action: WorkspaceAction,
): WorkspaceState {
  if (action.type === "open") {
    const existing = state.tabs.find(
      (tab) => tab.identity === action.tab.identity,
    );
    return existing
      ? { ...state, activeId: existing.tabId }
      : { tabs: [...state.tabs, action.tab], activeId: action.tab.tabId };
  }
  if (action.type === "activate")
    return state.tabs.some((tab) => tab.tabId === action.id)
      ? { ...state, activeId: action.id }
      : state;
  if (action.type === "identity") {
    const target = state.tabs.find((tab) => tab.tabId === action.id);
    if (!target || !/^[a-zA-Z0-9-]{1,80}$/.test(action.uid)) return state;
    const identity = `${target.pageKind}:${action.uid}`;
    // 已有另一实例时保留两份会话，由用户核对已保存状态，禁止静默合并或释放。
    if (
      state.tabs.some(
        (tab) => tab.tabId !== action.id && tab.identity === identity,
      )
    )
      return state;
    return {
      ...state,
      tabs: state.tabs.map((tab) =>
        tab.tabId === action.id
          ? { ...tab, identity, params: { id: action.uid } }
          : tab,
      ),
    };
  }
  const index = state.tabs.findIndex((tab) => tab.tabId === action.id);
  const tabs = state.tabs.filter((tab) => tab.tabId !== action.id);
  return {
    tabs,
    activeId:
      state.activeId === action.id
        ? (tabs[Math.min(index, tabs.length - 1)]?.tabId ?? null)
        : state.activeId,
  };
}
