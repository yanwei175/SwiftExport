import {
  Alert,
  App,
  Button,
  Card,
  Empty,
  Flex,
  Form,
  Input,
  Space,
  Spin,
  Splitter,
  Tag,
  Tooltip,
  Typography,
  theme,
} from "antd";
import { PlusOutlined, ReloadOutlined } from "@ant-design/icons";
import { useEffect, useRef } from "react";
import { ProForm } from "@ant-design/pro-components/form";
import { PreferenceDataGrid } from "../../../../shared/ui/偏好表格";
import { RecoveryActions } from "../../../../shared/ui/恢复操作";
import type { useDictionaryEditor } from "../字典编辑会话";
import "./字典布局.css";

/** 只呈现 feature 会话与受控组件，展示树隐藏时不负责销毁草稿或决定保存结果。 */
export function DictionaryManagementView({
  editor,
}: {
  editor: ReturnType<typeof useDictionaryEditor>;
}) {
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const shownNotice = useRef<typeof editor.notice>(null);
  // 临时反馈使用应用 message，不占业务布局；读取失败和未知保存仍有持久的页内出口。
  useEffect(() => {
    const notice = editor.notice;
    if (
      !notice ||
      notice === shownNotice.current ||
      notice.persistent ||
      (editor.phase === "unknown" && notice.type === "error")
    )
      return;
    shownNotice.current = notice;
    void message.open({
      key: `dictionary-feedback-${editor.tabId}`,
      type: notice.type,
      content: notice.text,
      duration: 4,
    });
  }, [editor.notice, editor.phase, editor.tabId, message]);
  return (
    <Flex
      vertical
      gap={8}
      className="dictionary-management"
      data-testid="dictionary-management"
      data-tab-id={editor.tabId}
    >
      <Flex align="center" justify="space-between" gap={12}>
        <Typography.Title level={4} style={{ margin: 0 }}>
          字典管理
        </Typography.Title>
        <Typography.Text type="secondary">
          保存状态：{editor.phase}
        </Typography.Text>
      </Flex>
      {editor.notice?.persistent && !editor.queryError && (
        <Alert
          type={editor.notice.type}
          title={editor.notice.text}
          showIcon
          action={
            <Button
              size="small"
              onClick={() => void editor.refresh()}
              disabled={editor.phase !== "idle"}
            >
              重试读取
            </Button>
          }
        />
      )}
      {editor.queryError && (
        <Alert
          type="error"
          title="字典类型读取失败"
          description={editor.queryError}
          action={
            <Button
              onClick={() => void editor.refresh()}
              disabled={editor.phase !== "idle"}
            >
              重试读取
            </Button>
          }
        />
      )}
      {editor.phase === "unknown" && (
        <RecoveryActions
          title="字典保存结果未知"
          description={
            <Space orientation="vertical" size="small">
              {editor.notice?.type === "error" && (
                <Typography.Text type="danger">
                  {editor.notice.text}
                </Typography.Text>
              )}
              <Typography.Text>
                冻结请求仍保留。没有自动重发或结果查询协议，请按现有合法业务/运维渠道核实；导出并明确确认保全后才能终止本地恢复。
              </Typography.Text>
            </Space>
          }
          onExport={editor.exportRecovery}
          onTerminate={() => void editor.terminateRecovery()}
        />
      )}
      {/* 分栏尺寸由 Ant Splitter 管理；拖宽只改变展示，不重建 Form 或编辑会话。 */}
      <Splitter className="dictionary-splitter" orientation="horizontal">
        <Splitter.Panel
          defaultSize={240}
          min={180}
          max="45%"
          style={{ paddingRight: 6 }}
        >
          <Card
            className="dictionary-type-card"
            title="字典类型"
            size="small"
            extra={
              <Space size={4}>
                <Tooltip title="刷新字典">
                  <Button
                    type="text"
                    aria-label="刷新字典"
                    icon={<ReloadOutlined />}
                    onClick={() => void editor.refresh()}
                    disabled={editor.phase !== "idle"}
                  />
                </Tooltip>
                <Tooltip title="新增字典类型">
                  <Button
                    type="text"
                    aria-label="新增字典类型"
                    icon={<PlusOutlined />}
                    onClick={() => void editor.addType()}
                    disabled={!editor.canCreateType}
                  />
                </Tooltip>
              </Space>
            }
          >
            <Flex
              vertical
              gap={8}
              className="dictionary-type-list"
              role="region"
              aria-label="字典类型列表"
              tabIndex={0}
            >
              {editor.types.map((type) => {
                const selected =
                  !editor.isNewType && editor.selectedType?.uid === type.uid;
                return (
                  <Button
                    key={type.uid ?? type.dictCode}
                    aria-label={`选择字典${type.dictCode}`}
                    aria-pressed={selected}
                    block
                    disabled={editor.phase !== "idle"}
                    onClick={() => editor.selectType(type)}
                    style={{
                      height: 60,
                      padding: "8px 12px",
                      textAlign: "left",
                      background: selected
                        ? token.controlItemBgActive
                        : undefined,
                      borderColor: selected ? token.colorPrimary : undefined,
                    }}
                  >
                    {/* Button 默认按行排列直接子节点；单一纵向容器保证所有项目始终两行且等高。 */}
                    <span
                      style={{
                        display: "flex",
                        flexDirection: "column",
                        alignItems: "stretch",
                        gap: 2,
                        width: "100%",
                        minWidth: 0,
                      }}
                    >
                      <Typography.Text
                        ellipsis
                        title={type.dictName || type.dictCode || undefined}
                      >
                        {type.dictName || type.dictCode}
                      </Typography.Text>
                      <Typography.Text
                        type="secondary"
                        ellipsis
                        title={type.dictCode ?? undefined}
                        style={{ fontSize: 12 }}
                      >
                        {type.dictCode}
                      </Typography.Text>
                    </span>
                  </Button>
                );
              })}
              {editor.loading && <Spin size="small" />}
              {!editor.types.length && !editor.loading && (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="暂无可显示的字典类型"
                />
              )}
            </Flex>
          </Card>
        </Splitter.Panel>
        <Splitter.Panel min="40%" style={{ paddingLeft: 6 }}>
          <Flex vertical gap={12} className="dictionary-detail">
            <Card
              className="dictionary-type-properties"
              title="字典类型属性"
              size="small"
              extra={
                editor.protectedType ? (
                  <Tag>系统类型只读</Tag>
                ) : editor.isNewType ? (
                  <Tag color="processing">新增类型</Tag>
                ) : null
              }
            >
              {editor.itemsNotice}
              {/* 实例首次展示即连接，类型读取成功前也不让 controller 对未连接 Form 重设字段。 */}
              <ProForm
                form={editor.form}
                submitter={false}
                omitNil={false}
                dateFormatter={false}
                syncToUrl={false}
                syncToInitialValues={false}
                autoFocusFirstInput={false}
                isKeyPressSubmit={false}
                layout="vertical"
                onValuesChange={editor.onTypeValuesChange}
                disabled={editor.typeReadOnly}
              >
                {editor.selectedType && (
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 16 }}>
                    <Form.Item
                      name="dictCode"
                      label="字典编码"
                      rules={[
                        {
                          required: true,
                          whitespace: true,
                          message: "字典编码不能为空。",
                        },
                      ]}
                      style={{ flex: "1 1 180px" }}
                    >
                      <Input aria-label="字典编码" />
                    </Form.Item>
                    <Form.Item
                      name="dictName"
                      label="字典名称"
                      rules={[
                        {
                          required: true,
                          whitespace: true,
                          message: "字典名称不能为空。",
                        },
                      ]}
                      style={{ flex: "1 1 180px" }}
                    >
                      <Input aria-label="字典名称" />
                    </Form.Item>
                    <Form.Item
                      name="sortOrder"
                      label="类型排序"
                      rules={[
                        { required: true, message: "类型排序不能为空。" },
                      ]}
                      style={{ flex: "0 1 120px" }}
                    >
                      <Input aria-label="类型排序" inputMode="numeric" />
                    </Form.Item>
                    <Form.Item
                      name="remark"
                      label="类型备注"
                      style={{ flex: "2 1 240px" }}
                    >
                      <Input aria-label="类型备注" />
                    </Form.Item>
                  </div>
                )}
              </ProForm>
              {editor.selectedType ? (
                <Space wrap>
                  <Button
                    type="primary"
                    onClick={() => void editor.saveType()}
                    disabled={!editor.canSaveType}
                  >
                    保存类型
                  </Button>
                  <Button
                    onClick={() => void editor.discardType()}
                    disabled={!editor.canDiscardType}
                  >
                    放弃类型变更
                  </Button>
                  <Button
                    danger
                    onClick={() => void editor.deleteType()}
                    disabled={!editor.canDeleteType}
                  >
                    删除类型
                  </Button>
                </Space>
              ) : (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="选择字典类型，或新增一个自定义类型。"
                />
              )}
            </Card>
            <Card title="字典项" size="small" className="dictionary-items-card">
              <Flex vertical gap={8} className="dictionary-items-content">
                <Typography.Text type="secondary">
                  {editor.selectedType?.dictCode ?? "尚未选择类型"}
                  {editor.isNewType ? "；请先保存新类型。" : ""}
                </Typography.Text>
                <Space wrap>
                  <Button
                    onClick={editor.addItem}
                    disabled={!editor.canCreateItem}
                  >
                    新增字典项
                  </Button>
                  <Button
                    type="primary"
                    onClick={() => void editor.saveItems()}
                    disabled={!editor.canSaveItems}
                  >
                    保存字典项
                  </Button>
                  <Button
                    danger
                    onClick={editor.deleteItems}
                    disabled={!editor.canDeleteItems}
                  >
                    删除选中项
                  </Button>
                  <Button
                    onClick={editor.restoreItems}
                    disabled={!editor.canRestoreItems}
                  >
                    恢复删除项
                  </Button>
                  <Button
                    onClick={() => void editor.discardItems()}
                    disabled={!editor.canDiscardItems}
                  >
                    放弃字典项变更
                  </Button>
                </Space>
                <PreferenceDataGrid
                  fillHeight
                  preferenceId="system.dictionary.items"
                  rows={editor.rows}
                  getRowKey={(row) => row.clientKey}
                  columns={editor.columns}
                  readOnly={editor.itemsReadOnly}
                  canEdit={(row, columnKey) =>
                    editor.rowCanEdit(row, columnKey)
                  }
                  selectedKeys={editor.selectedKeys}
                  onSelectionChange={editor.onSelectionChange}
                  onFillDown={editor.fillDown}
                  showRowNumber
                  loading={editor.phase === "loading"}
                />
                <Typography.Text
                  type={
                    editor.summary.dirty || editor.summary.hasUncommittedInput
                      ? "warning"
                      : "secondary"
                  }
                >
                  {editor.summary.dirty || editor.summary.hasUncommittedInput
                    ? "有未保存修改或未提交输入"
                    : "当前没有未保存修改"}
                </Typography.Text>
              </Flex>
            </Card>
          </Flex>
        </Splitter.Panel>
      </Splitter>
    </Flex>
  );
}
