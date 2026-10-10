import { useEffect, useRef, useState } from "react";
import { Alert, App, Button, Flex, Input, Space, Typography } from "antd";
import { PlusOutlined, ReloadOutlined, SaveOutlined } from "@ant-design/icons";
import type { WorkspaceTabProps } from "../../../shared/workspace/页签契约";
import { PreferenceDataGrid } from "../../../shared/ui/偏好表格";
import { RecoveryActions } from "../../../shared/ui/恢复操作";
import { useSupplierEditor } from "./供应商编辑会话";
import "./供应商布局.css";

/** 供应商展示只消费原页签会话；轻提示使用 Ant App，读取失败和未知恢复保留页内入口。 */
function SupplierManagementView({
  editor,
}: {
  editor: ReturnType<typeof useSupplierEditor>;
}) {
  const [keyword, setKeyword] = useState("");
  const { message } = App.useApp();
  const shown = useRef<typeof editor.notice>(null);
  useEffect(() => {
    const notice = editor.notice;
    if (
      !notice ||
      notice === shown.current ||
      notice.persistent ||
      (editor.phase === "unknown" && notice.type === "error")
    )
      return;
    shown.current = notice;
    void message.open({
      key: `supplier-feedback-${editor.tabId}`,
      type: notice.type,
      content: notice.text,
      duration: 4,
    });
  }, [editor.notice, editor.phase, editor.tabId, message]);
  return (
    <Flex
      vertical
      gap={8}
      className="supplier-management"
      data-testid="supplier-management"
      data-supplier-tab={editor.tabId}
      data-tab-id={editor.tabId}
    >
      <Flex align="center" justify="space-between" gap={12}>
        <Typography.Title level={4} style={{ margin: 0 }}>
          供应商管理
        </Typography.Title>
        <Typography.Text type="secondary">
          保存状态：{editor.phase}
        </Typography.Text>
      </Flex>
      {editor.error || editor.notice?.persistent ? (
        <Alert
          type="error"
          title={editor.error ?? editor.notice?.text}
          showIcon
          action={
            <Button
              size="small"
              disabled={editor.phase !== "idle"}
              onClick={() => void editor.refresh()}
            >
              重试读取
            </Button>
          }
        />
      ) : null}
      {editor.phase === "unknown" && (
        <RecoveryActions
          title="供应商保存结果待核实"
          description={
            <Space orientation="vertical">
              <Typography.Text>
                {editor.notice?.text ?? "服务器结果仍未确认。"}
              </Typography.Text>
              <Typography.Text type="secondary">
                冻结请求仍保留。未确认失败或回滚前不能重发；可独立读取辅助核实，保全后人工终止仅释放本地草稿。
              </Typography.Text>
              <Button onClick={() => void editor.inspectRecovery()}>
                只读核实
              </Button>
            </Space>
          }
          onExport={editor.exportRecovery}
          onTerminate={() => void editor.terminateRecovery()}
        />
      )}
      <Flex gap={8} wrap="wrap" align="center">
        <Input
          aria-label="供应商查询"
          placeholder="代码、名称、关联号或联系人"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          onPressEnter={() => void editor.query(keyword)}
          disabled={editor.readOnly}
          style={{ width: 300, maxWidth: "100%" }}
          allowClear
        />
        <Button
          disabled={editor.readOnly}
          onClick={() => void editor.query(keyword)}
        >
          查询
        </Button>
        <Button
          aria-label="刷新"
          title="刷新供应商"
          icon={<ReloadOutlined />}
          disabled={editor.readOnly}
          onClick={() => void editor.refresh()}
        />
      </Flex>
      <Space wrap>
        <Button
          icon={<PlusOutlined />}
          disabled={!editor.canCreate}
          onClick={editor.add}
        >
          新增供应商
        </Button>
        <Button
          type="primary"
          icon={<SaveOutlined />}
          disabled={!editor.canSave}
          loading={editor.phase === "preparing" || editor.phase === "saving"}
          onClick={() => void editor.save()}
        >
          保存
        </Button>
        <Button
          danger
          disabled={!editor.canDelete}
          onClick={editor.deleteSelected}
        >
          删除选中
        </Button>
        <Button disabled={!editor.canRestore} onClick={editor.restoreDeleted}>
          恢复删除
        </Button>
        <Button
          disabled={!editor.canDiscard}
          onClick={() => void editor.discard()}
        >
          放弃修改
        </Button>
      </Space>
      <div className="supplier-grid">
        <PreferenceDataGrid
          preferenceId="supplier-management"
          rows={editor.rows}
          columns={editor.columns}
          getRowKey={(row) => row.clientKey}
          selectedKeys={editor.selectedKeys}
          onSelectionChange={editor.onSelectionChange}
          readOnly={editor.readOnly}
          canEdit={editor.rowCanEdit}
          onFillDown={editor.fillDown}
          pagination={editor.pagination}
          loading={editor.loading}
          fillHeight
        />
      </div>
      <Typography.Text type="secondary" aria-live="polite">
        业务修改：{editor.summary.dirty ? "是" : "否"}；未提交输入：
        {editor.summary.hasUncommittedInput ? "是" : "否"}
      </Typography.Text>
    </Flex>
  );
}

/** 编辑会话在 Activity 外保活，供应商列表按 pageKind 单例定位，隐藏不会丢失缓冲。 */
export default function SupplierManagementTab(props: WorkspaceTabProps) {
  const editor = useSupplierEditor(props);
  return props.renderDisplay(<SupplierManagementView editor={editor} />);
}
