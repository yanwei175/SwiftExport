import { Button, Space, Tabs, Tag } from "antd";
import { PageState } from "../shared/ui/页面状态";
import { useWorkspace } from "./工作区Provider";
import { TabFrame } from "./页签容器";

/** Tabs 负责页签栏；稳定容器驻留 feature，会话生命周期不由 Tabs 隐藏策略决定。 */
export function WorkspaceView() {
  const workspace = useWorkspace();
  return (
    <section className="workspace" aria-label="多标签工作区">
      <div className="workspace-toolbar">
        <Space>
          <Button
            onClick={() =>
              void workspace.close(
                workspace.tabs
                  .filter((tab) => tab.tabId !== workspace.activeId)
                  .map((tab) => tab.tabId),
              )
            }
          >
            关闭其他
          </Button>
          <Button
            onClick={() =>
              void workspace.close(workspace.tabs.map((tab) => tab.tabId))
            }
          >
            关闭全部
          </Button>
        </Space>
      </div>
      <Tabs
        type="editable-card"
        hideAdd
        destroyOnHidden={false}
        activeKey={workspace.activeId ?? undefined}
        onChange={workspace.activate}
        onEdit={(id, action) => {
          if (action === "remove" && typeof id === "string")
            void workspace.close([id]);
        }}
        items={workspace.tabs.map((tab) => {
          const summary = workspace.summaries.get(tab.tabId);
          const protectedInput = summary?.dirty || summary?.hasUncommittedInput;
          return {
            key: tab.tabId,
            label: (
              <span>
                {tab.title}
                {protectedInput ? " *" : ""}
                {summary?.unknown && <Tag color="warning">待核实</Tag>}
                {summary?.saving && <Tag color="processing">保存中</Tag>}
              </span>
            ),
            closable: true,
          };
        })}
      />
      {workspace.tabs.length === 0 && (
        <PageState kind="empty" description="工作区为空，请从导航打开页面。" />
      )}
      {workspace.tabs.map((tab) => (
        <TabFrame
          key={tab.tabId}
          tab={tab}
          active={tab.tabId === workspace.activeId}
        />
      ))}
    </section>
  );
}
