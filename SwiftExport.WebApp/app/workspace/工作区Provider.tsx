import { App } from "antd";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
} from "react";
import type { ReactNode } from "react";
import { useBlocker, useLocation, useNavigate } from "react-router";
import { useSession } from "../shared/session/会话上下文";
import { emptyProtection, isUnprotected } from "../shared/workspace/页签契约";
import type {
  OpenIntent,
  ProtectionSummary,
  TabControl,
  TabDescriptor,
  TabHost,
} from "../shared/workspace/页签契约";
import { createDescriptor, pageDirectory, validIntent } from "./页面目录";
import { parseTabAddress, tabAddress } from "./活动页地址";
import { workspaceReducer } from "./工作区状态";

/** 宿主公共状态不携带业务 DTO，命令按稳定页签实例调用。 */
interface WorkspaceContextValue {
  tabs: readonly TabDescriptor[];
  activeId: string | null;
  summaries: ReadonlyMap<string, ProtectionSummary>;
  open(intent: OpenIntent): void;
  activate(id: string): void;
  close(ids: readonly string[]): Promise<boolean>;
  host(tab: TabDescriptor): TabHost;
  markVisible(id: string, ready: boolean): void;
  markFailed(id: string): void;
  leave(action: () => void): Promise<void>;
}
const WorkspaceContext = createContext<WorkspaceContextValue | null>(null);

/** 获取应用工作区组合入口，feature 必须使用 shared 的窄宿主契约。 */
export function useWorkspace(): WorkspaceContextValue {
  const value = useContext(WorkspaceContext);
  if (!value) throw new Error("缺少工作区上下文。");
  return value;
}

/** 保存/放弃/取消确认使用 Ant 宿主，三种选择不会被模态取消误映射。 */
function chooseClose(
  modal: ReturnType<typeof App.useApp>["modal"],
  title: string,
): Promise<"save" | "discard" | "cancel"> {
  return new Promise((resolve) => {
    const dialog = modal.confirm({
      title: `${title}有未保存输入`,
      content: "保存后关闭，或明确放弃本页输入。",
      okText: "保存并关闭",
      cancelText: "取消",
      onOk: () => resolve("save"),
      onCancel: () => resolve("cancel"),
      footer: (_, { OkBtn, CancelBtn }) => (
        <>
          <button
            className="discard-button"
            onClick={() => {
              dialog.destroy();
              resolve("discard");
            }}
          >
            放弃并关闭
          </button>
          <CancelBtn />
          <OkBtn />
        </>
      ),
    });
  });
}

/** 统一协调关闭、离开与输入保护；隐藏页守卫不随展示 Effects 注销。 */
export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(workspaceReducer, {
    tabs: [],
    activeId: null,
  });
  const stateRef = useRef(state);
  stateRef.current = state;
  const controls = useRef(new Map<string, TabControl>());
  const hosts = useRef(new Map<string, TabHost>());
  const summariesRef = useRef(new Map<string, ProtectionSummary>());
  const [summaries, setSummaries] = useState<
    ReadonlyMap<string, ProtectionSummary>
  >(new Map());
  const visibility = useRef(new Set<string>());
  const waiters = useRef(
    new Map<string, Set<{ resolve(): void; reject(error: Error): void }>>(),
  );
  const failedDisplays = useRef(new Set<string>());
  const closing = useRef(false);
  const alive = useRef(true);
  const { modal, message } = App.useApp();
  const session = useSession();
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const navigate = useNavigate();
  const location = useLocation();
  const rejectVisibility = useCallback((id: string, reason: string) => {
    visibility.current.delete(id);
    for (const waiter of waiters.current.get(id) ?? [])
      waiter.reject(new Error(reason));
    waiters.current.delete(id);
  }, []);
  const canRead = useCallback((tab: Pick<TabDescriptor, "pageKind">) => {
    const current = sessionRef.current;
    const definition = pageDirectory[tab.pageKind];
    return (
      current.status === "authenticated" &&
      Boolean(definition) &&
      (!definition?.permission ||
        current.user?.permissions?.includes(definition.permission))
    );
  }, []);
  const open = useCallback(
    (intent: OpenIntent) => {
      if (!validIntent(intent) || !canRead(intent)) {
        void message.warning("页面不可用或没有访问权限。");
        return;
      }
      const descriptor = createDescriptor(intent);
      dispatch({ type: "open", tab: descriptor });
      void navigate(`/workspace${tabAddress(descriptor)}`);
    },
    [canRead, message, navigate],
  );
  const activate = useCallback(
    (id: string) => {
      const tab = stateRef.current.tabs.find((item) => item.tabId === id);
      if (!tab) return;
      dispatch({ type: "activate", id });
      void navigate(`/workspace${tabAddress(tab)}`);
    },
    [navigate],
  );
  const remove = useCallback(
    (id: string) => {
      rejectVisibility(id, "页签实例已释放，本次保存准备没有派发。");
      failedDisplays.current.delete(id);
      const next = workspaceReducer(stateRef.current, { type: "remove", id });
      stateRef.current = next;
      dispatch({ type: "remove", id });
      controls.current.delete(id);
      hosts.current.delete(id);
      summariesRef.current.delete(id);
      setSummaries(new Map(summariesRef.current));
      const active = next.tabs.find((tab) => tab.tabId === next.activeId);
      void navigate(active ? `/workspace${tabAddress(active)}` : "/workspace", {
        replace: true,
      });
    },
    [navigate, rejectVisibility],
  );
  const close = useCallback(
    async (ids: readonly string[]) => {
      if (closing.current) return false;
      closing.current = true;
      const expectedEpoch = sessionRef.current.identityEpoch;
      try {
        for (const id of ids) {
          const tab = stateRef.current.tabs.find((item) => item.tabId === id);
          if (!tab) continue;
          let control = controls.current.get(id);
          let summary =
            control?.getSummary() ??
            summariesRef.current.get(id) ??
            emptyProtection();
          const requiredHandling = !isUnprotected(summary);
          if (summary.preparing || summary.saving || summary.unknown) {
            void message.warning(
              summary.unknown
                ? "结果未知，请在原页核实或处理恢复记录。"
                : "该页正在保存，请等待结果。",
            );
            return false;
          }
          if (!isUnprotected(summary)) {
            if (!canRead(tab)) {
              void message.warning(
                "当前无权处理本页输入，请通过现有合法业务渠道核实。",
              );
              return false;
            }
            const decision = await chooseClose(modal, tab.title);
            if (decision === "cancel") return false;
            if (
              !alive.current ||
              sessionRef.current.identityEpoch !== expectedEpoch ||
              !canRead(tab)
            )
              return false;
            control = controls.current.get(id);
            if (!control) return false;
            const result =
              decision === "save"
                ? await control.requestSave()
                : await control.requestDiscard();
            if (result !== "saved" && result !== "discarded") {
              activate(id);
              return false;
            }
          }
          // 回调期间可能出现新输入，实际移除前必须复核最新完整摘要及权限。
          control = controls.current.get(id);
          summary =
            control?.getSummary() ??
            summariesRef.current.get(id) ??
            emptyProtection();
          if (
            !alive.current ||
            sessionRef.current.identityEpoch !== expectedEpoch ||
            !isUnprotected(summary) ||
            (control !== undefined && !control.canClose().allowed) ||
            (requiredHandling && (!control || !canRead(tab)))
          )
            return false;
          remove(id);
        }
        return true;
      } finally {
        closing.current = false;
      }
    },
    [activate, canRead, message, modal, remove],
  );
  const markVisible = useCallback(
    (id: string, ready: boolean) => {
      if (!ready) {
        rejectVisibility(id, "页面展示已暂停，本次保存准备没有派发。");
        return;
      }
      visibility.current.add(id);
      for (const waiter of waiters.current.get(id) ?? []) waiter.resolve();
      waiters.current.delete(id);
    },
    [rejectVisibility],
  );
  const markFailed = useCallback(
    (id: string) => {
      failedDisplays.current.add(id);
      rejectVisibility(id, "页面展示失败，保留原输入，本次保存准备没有派发。");
    },
    [rejectVisibility],
  );
  const host = useCallback(
    (tab: TabDescriptor): TabHost => {
      const existing = hosts.current.get(tab.tabId);
      if (existing) return existing;
      const ownerEpoch = sessionRef.current.identityEpoch;
      const value: TabHost = {
        registerControl(control) {
          controls.current.set(tab.tabId, control);
          return () => {
            if (controls.current.get(tab.tabId) === control)
              controls.current.delete(tab.tabId);
          };
        },
        reportSummary(summary) {
          summariesRef.current.set(tab.tabId, summary);
          setSummaries(new Map(summariesRef.current));
        },
        async ensureVisible() {
          if (!canRead(tab)) throw new Error("当前没有页面访问权限。");
          if (failedDisplays.current.has(tab.tabId))
            throw new Error("页面展示不可用，不能开始保存。");
          activate(tab.tabId);
          if (visibility.current.has(tab.tabId)) return;
          await new Promise<void>((resolve, reject) => {
            const queue = waiters.current.get(tab.tabId) ?? new Set();
            queue.add({ resolve, reject });
            waiters.current.set(tab.tabId, queue);
          });
        },
        open,
        updateBusinessIdentity(uid) {
          // 已派发保存的身份回执属于原会话；401/撤权遮蔽内容，但不丢弃已确认定位。
          // 此动作只改定位元数据，不读取业务内容或扩大权限，换身份则拒绝旧回写。
          if (!alive.current || sessionRef.current.identityEpoch !== ownerEpoch)
            return false;
          const previous = stateRef.current;
          const next = workspaceReducer(previous, {
            type: "identity",
            id: tab.tabId,
            uid,
          });
          if (next === previous) {
            void message.warning(
              "实体身份不可更新或已有其他页签；两页均保留，请核对已保存状态后处理。",
            );
            return false;
          }
          stateRef.current = next;
          dispatch({ type: "identity", id: tab.tabId, uid });
          const updated = next.tabs.find((item) => item.tabId === tab.tabId)!;
          if (next.activeId === tab.tabId)
            void navigate(`/workspace${tabAddress(updated)}`, {
              replace: true,
            });
          return true;
        },
        closeAfterManualTermination(recordId) {
          if (
            !recordId ||
            closing.current ||
            !canRead(tab) ||
            !controls.current.get(tab.tabId)?.getSummary().unknown
          )
            return false;
          // 仅 feature 已完成授权保全和双重确认的专用出口，不转成 saved/clean。
          remove(tab.tabId);
          return true;
        },
      };
      hosts.current.set(tab.tabId, value);
      return value;
    },
    [activate, canRead, message, navigate, open, remove],
  );
  useEffect(() => {
    alive.current = true;
    const pending = waiters.current;
    return () => {
      alive.current = false;
      for (const id of pending.keys())
        rejectVisibility(id, "工作区会话已销毁，停止本次准备。");
    };
  }, [rejectVisibility]);
  useEffect(() => {
    for (const id of waiters.current.keys()) {
      const tab = stateRef.current.tabs.find((item) => item.tabId === id);
      if (!tab || !canRead(tab))
        rejectVisibility(id, "身份或读取权限已失效，保留输入且未发送提交。");
    }
  }, [
    session.status,
    session.user,
    session.identityEpoch,
    canRead,
    rejectVisibility,
  ]);
  useEffect(() => {
    const intent = parseTabAddress(location.search);
    if (intent && canRead(intent))
      dispatch({ type: "open", tab: createDescriptor(intent) });
    else if (location.search && !intent)
      void message.warning({
        content: "页面地址参数无效。",
        key: "workspace-address-invalid",
      });
  }, [location.search, canRead, message, session.status]);
  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => {
      if (
        [...controls.current.values()].some(
          (control) => !isUnprotected(control.getSummary()),
        )
      ) {
        event.preventDefault();
        event.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", unload);
    return () => window.removeEventListener("beforeunload", unload);
  }, []);
  const hasProtected = () =>
    [...controls.current.values()].some(
      (control) => !isUnprotected(control.getSummary()),
    );
  const blocker = useBlocker(
    ({ nextLocation }) =>
      nextLocation.pathname !== "/workspace" && hasProtected(),
  );
  const blockerRef = useRef(blocker);
  blockerRef.current = blocker;
  const handlingNavigation = useRef(false);
  useEffect(() => {
    if (blocker.state !== "blocked" || handlingNavigation.current) return;
    handlingNavigation.current = true;
    void close(stateRef.current.tabs.map((tab) => tab.tabId))
      .then((allowed) => {
        const current = blockerRef.current;
        if (current.state !== "blocked") return;
        // 等待保存时允许新开其他页；离开前必须复核当前所有页，不能只看启动快照。
        const remainingProtected = [...controls.current.values()].some(
          (control) => !isUnprotected(control.getSummary()),
        );
        if (allowed && !remainingProtected) current.proceed();
        else current.reset();
      })
      .finally(() => {
        handlingNavigation.current = false;
      });
  }, [blocker, close]);
  const leave = useCallback(
    async (action: () => void) => {
      const expectedEpoch = sessionRef.current.identityEpoch;
      if (
        !(await close(stateRef.current.tabs.map((tab) => tab.tabId))) ||
        !alive.current ||
        sessionRef.current.identityEpoch !== expectedEpoch
      )
        return;
      if (
        [...controls.current.values()].some(
          (control) => !isUnprotected(control.getSummary()),
        )
      ) {
        void message.warning("其他页签出现新的未保存输入，请处理后再离开。");
        return;
      }
      action();
    },
    [close, message],
  );
  const value = useMemo(
    () => ({
      ...state,
      summaries,
      open,
      activate,
      close,
      host,
      markVisible,
      markFailed,
      leave,
    }),
    [
      state,
      summaries,
      open,
      activate,
      close,
      host,
      markVisible,
      markFailed,
      leave,
    ],
  );
  return (
    <WorkspaceContext.Provider value={value}>
      {children}
    </WorkspaceContext.Provider>
  );
}
