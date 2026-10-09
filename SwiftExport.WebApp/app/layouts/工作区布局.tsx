import {
  HomeOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  ExperimentOutlined,
} from "@ant-design/icons";
import { App, Button, Layout, Menu, Space, Typography } from "antd";
import { useState } from "react";
import { Link } from "react-router";
import { useSession } from "../shared/session/会话上下文";
import { useWorkspace } from "../workspace/工作区Provider";
import { WorkspaceView } from "../workspace/工作区视图";
import { pageDirectory, probesEnabled } from "../workspace/页面目录";

/** 工作区布局组合导航、页签和账户入口，业务保存留在 feature。 */
export function WorkspaceLayout() {
  const [collapsed, setCollapsed] = useState(false);
  const session = useSession();
  const workspace = useWorkspace();
  const { message, notification, modal } = App.useApp();
  const probeAllowed =
    probesEnabled &&
    session.user?.permissions?.includes(
      pageDirectory["verification-form"]?.permission ?? "",
    );
  return (
    <Layout className="app-layout">
      <Layout.Sider
        collapsed={collapsed}
        width={220}
        theme="light"
        breakpoint="lg"
        onBreakpoint={(broken) => setCollapsed(broken)}
      >
        <div className="brand">{collapsed ? "SE" : "SwiftExport"}</div>
        <Menu
          mode="inline"
          selectedKeys={[
            workspace.tabs.find((tab) => tab.tabId === workspace.activeId)
              ?.pageKind ?? "",
          ]}
          items={[
            {
              key: "home",
              icon: <HomeOutlined />,
              label: "首页",
              onClick: () => workspace.open({ pageKind: "home" }),
            },
            ...(probeAllowed
              ? [
                  {
                    key: "verification",
                    icon: <ExperimentOutlined />,
                    label: "骨架验收",
                    children: [
                      {
                        key: "verification-form",
                        label: "表单验收",
                        onClick: () =>
                          workspace.open({
                            pageKind: "verification-form",
                            params: { id: "form-a" },
                          }),
                      },
                      {
                        key: "verification-grid",
                        label: "列表验收",
                        onClick: () =>
                          workspace.open({
                            pageKind: "verification-grid",
                            params: { id: "grid-a" },
                          }),
                      },
                    ],
                  },
                ]
              : []),
          ]}
        />
      </Layout.Sider>
      <Layout>
        <Layout.Header className="app-header">
          <Space>
            <Button
              aria-label="折叠导航"
              type="text"
              icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
              onClick={() => setCollapsed(!collapsed)}
            />
            <Typography.Text strong>SwiftExport 工作区</Typography.Text>
          </Space>
          <Space>
            <Typography.Text>
              {session.user?.nickName ?? session.user?.userName}
            </Typography.Text>
            <Button
              onClick={() =>
                void session
                  .refreshPermissions()
                  .catch(() => message.error("权限刷新失败，请重试。"))
              }
            >
              刷新权限
            </Button>
            <Button onClick={() => void workspace.leave(session.logout)}>
              退出登录
            </Button>
          </Space>
        </Layout.Header>
        <Layout.Content className="app-content">
          <WorkspaceView />
        </Layout.Content>
        {probeAllowed && (
          <Layout.Footer className="app-footer">
            <Space wrap>
              <Typography.Text type="secondary">骨架验收模式</Typography.Text>
              <Button
                onClick={() =>
                  workspace.open({
                    pageKind: "verification-form",
                    params: { draft: crypto.randomUUID() },
                  })
                }
              >
                新建验收单
              </Button>
              <Button onClick={() => void message.success("消息提示可用")}>
                验证 message
              </Button>
              <Button
                onClick={() =>
                  notification.info({
                    title: "通知提示",
                    description: "主题与中文上下文已接入。",
                  })
                }
              >
                验证 notification
              </Button>
              <Button
                onClick={() =>
                  modal.info({
                    title: "弹窗提示",
                    content: "应用弹窗上下文已接入。",
                  })
                }
              >
                验证 Modal
              </Button>
              <Link to="/not-found">验证离开工作区</Link>
            </Space>
          </Layout.Footer>
        )}
      </Layout>
    </Layout>
  );
}
