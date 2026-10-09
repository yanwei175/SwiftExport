import { Activity, Component, Suspense, useLayoutEffect } from "react";
import type { ReactNode } from "react";
import { PageState } from "../shared/ui/页面状态";
import { TabHostContext } from "../shared/workspace/useWorkspaceTab";
import { useSession } from "../shared/session/会话上下文";
import type { TabDescriptor } from "../shared/workspace/页签契约";
import { pageDirectory } from "./页面目录";
import { useWorkspace } from "./工作区Provider";
import { recordDiagnostic } from "../shared/diagnostics/诊断记录";

/** 页面错误仅隔离本页，不销毁其他页签及其编辑会话。 */
class TabErrorBoundary extends Component<
  { children: ReactNode; onFailure?: () => void },
  { failed: boolean }
> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  /** 展示错误结束显式握手，避免隐藏保存永远占用准备锁。 */
  componentDidCatch() {
    recordDiagnostic({
      event: "render-failed",
      level: "error",
      kind: "unexpected",
    });
    this.props.onFailure?.();
  }
  render() {
    return this.state.failed ? (
      <PageState kind="error" title="该页暂时无法显示，其他页签仍可使用。" />
    ) : (
      this.props.children
    );
  }
}

/** Activity 显示后显式握手，隐藏清理只撤销展示就绪，不注销关闭控制器。 */
function DisplayBoundary({
  id,
  children,
}: {
  id: string;
  children: ReactNode;
}) {
  const { markVisible } = useWorkspace();
  useLayoutEffect(() => {
    markVisible(id, true);
    return () => markVisible(id, false);
  }, [id, markVisible]);
  return (
    <div className="tab-display" data-testid={`display-${id}`}>
      {children}
    </div>
  );
}

/** 稳定实例包围 feature 控制器，由注入函数仅包裹其展示树。 */
export function TabFrame({
  tab,
  active,
}: {
  tab: TabDescriptor;
  active: boolean;
}) {
  const workspace = useWorkspace();
  const session = useSession();
  const definition = pageDirectory[tab.pageKind];
  if (!definition) return null;
  const permitted =
    session.status === "authenticated" &&
    (!definition.permission ||
      session.user?.permissions?.includes(definition.permission));
  const Entry = definition.component;
  return (
    <TabHostContext.Provider value={workspace.host(tab)}>
      <TabErrorBoundary>
        <Suspense fallback={<PageState kind="loading" />}>
          <Entry
            descriptor={tab}
            isActive={active && Boolean(permitted)}
            renderDisplay={(children) => (
              <Activity mode={active && permitted ? "visible" : "hidden"}>
                <TabErrorBoundary
                  onFailure={() => workspace.markFailed(tab.tabId)}
                >
                  <DisplayBoundary id={tab.tabId}>{children}</DisplayBoundary>
                </TabErrorBoundary>
              </Activity>
            )}
          />
          {active && !permitted && (
            <PageState
              kind="forbidden"
              title="当前没有页面读取权限"
              description="原输入保持保护，请通过现有合法业务渠道处理。"
            />
          )}
        </Suspense>
      </TabErrorBoundary>
    </TabHostContext.Provider>
  );
}
