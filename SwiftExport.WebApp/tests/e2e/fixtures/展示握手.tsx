import { createRoot } from "react-dom/client";
import { createMemoryRouter, RouterProvider } from "react-router";
import { Button, Space, Typography } from "antd";
import { ApplicationProviders } from "../../../app/bootstrap/应用Providers";
import { LoginForm } from "../../../app/features/auth/登录表单";
import VerificationTab from "../../../app/features/workspace-verification/验证页签入口";
import { useSession } from "../../../app/shared/session/会话上下文";
import { TabHostContext } from "../../../app/shared/workspace/useWorkspaceTab";
import {
  useWorkspace,
  WorkspaceProvider,
} from "../../../app/workspace/工作区Provider";

/** 只控制展示握手时机，保存、会话、权限和等待拒绝均使用真实生产组件。 */
function ControlledDisplayHarness() {
  const session = useSession();
  const workspace = useWorkspace();
  const tab = workspace.tabs[0];
  if (session.status === "anonymous")
    return (
      <main>
        <LoginForm />
      </main>
    );
  return (
    <main>
      <Space wrap>
        <Button
          onClick={() =>
            workspace.open({
              pageKind: "verification-grid",
              params: { id: "handshake" },
            })
          }
        >
          打开握手验收页
        </Button>
        <Button
          onClick={() =>
            void session.refreshPermissions().catch(() => undefined)
          }
        >
          读取身份变化
        </Button>
        <Button
          disabled={!tab}
          onClick={() => tab && workspace.markVisible(tab.tabId, true)}
        >
          确认展示就绪
        </Button>
        <Button
          disabled={!tab}
          onClick={() => tab && workspace.markVisible(tab.tabId, false)}
        >
          中断展示
        </Button>
        <Button
          disabled={!tab}
          onClick={() => tab && workspace.markFailed(tab.tabId)}
        >
          报告展示失败
        </Button>
      </Space>
      <Typography.Paragraph data-testid="session-status">
        {session.status}
      </Typography.Paragraph>
      {tab && (
        <>
          <Typography.Paragraph data-testid="host-summary">
            {JSON.stringify(workspace.summaries.get(tab.tabId))}
          </Typography.Paragraph>
          <TabHostContext.Provider value={workspace.host(tab)}>
            <VerificationTab
              descriptor={tab}
              isActive
              renderDisplay={(children) => children}
            />
          </TabHostContext.Provider>
        </>
      )}
    </main>
  );
}

/** 内存 Data Router 只服务此测试入口，不注册到应用页面目录或发布构建。 */
const router = createMemoryRouter(
  [
    {
      path: "/workspace",
      element: (
        <ApplicationProviders>
          <WorkspaceProvider>
            <ControlledDisplayHarness />
          </WorkspaceProvider>
        </ApplicationProviders>
      ),
    },
  ],
  { initialEntries: ["/workspace"] },
);
createRoot(document.getElementById("root")!).render(
  <RouterProvider router={router} />,
);
