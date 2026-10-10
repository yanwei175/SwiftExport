import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Descriptions,
  Form,
  Input,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
} from "antd";
import { RecoveryActions } from "../../../shared/ui/恢复操作";
import { useSession } from "../../../shared/session/会话上下文";
import { hasPermission } from "../../../shared/authorization/权限检查";
import { useWorkspaceTab } from "../../../shared/workspace/useWorkspaceTab";
import {
  emptyProtection,
  isUnprotected,
} from "../../../shared/workspace/页签契约";
import type {
  ProtectionSummary,
  TabControl,
  SaveDecision,
  WorkspaceTabProps,
} from "../../../shared/workspace/页签契约";
import { settingsEditors } from "./页面定义";
import type { SettingsEditorKind } from "./页面定义";
import { fetchEditorSource, useSettingsQuery } from "./queries";
import type { SettingsEditorSource } from "./queries";
import {
  describeSettingsReadError,
  freezeSettingsSubmission,
  initialSettingsValues,
  projectSettingsValues,
  settingsRecoveryRecord,
  submitSettings,
  validateSettingsValues,
} from "./提交契约";
import type {
  FrozenSettingsSubmission,
  SettingsFormValues,
  SettingsSaveEvidence,
} from "./提交契约";
import "./设置布局.css";

/** 系统设置普通 Form 会话位于 Activity 外；展示隐藏不能重置字段、守卫或派发中的提交。 */
export default function SettingsEditorTab({
  descriptor,
  isActive,
  renderDisplay,
}: WorkspaceTabProps) {
  const kind = descriptor.pageKind as SettingsEditorKind;
  const definition = settingsEditors[kind];
  const id = descriptor.params.id ?? "";
  const session = useSession();
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const owner = useRef({
    userId: session.user?.userId,
    epoch: session.identityEpoch,
  });
  const host = useWorkspaceTab();
  const { message, modal } = App.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<SettingsFormValues>();
  const [source, setSource] = useState<SettingsEditorSource>({});
  const sourceRef = useRef(source);
  sourceRef.current = source;
  const baseline = useRef<SettingsFormValues>({});
  const loadedRef = useRef(false);
  const [loaded, setLoaded] = useState(false);
  const [phase, setPhaseState] = useState<
    "idle" | "preparing" | "saving" | "unknown"
  >("idle");
  const phaseRef = useRef(phase);
  const [completed, setCompleted] = useState(false);
  const completedRef = useRef(false);
  const summary = useRef<ProtectionSummary>(emptyProtection());
  const control = useRef<TabControl | null>(null);
  const attempt = useRef<FrozenSettingsSubmission | null>(null);
  const evidence = useRef<SettingsSaveEvidence | null>(null);
  const recordId = useRef<string | null>(null);
  const [notice, setNotice] = useState("");
  const [createdId, setCreatedId] = useState<string>();
  const canReadRoles =
    session.status === "authenticated" &&
    hasPermission(session.user, "System.Roles.View");
  const canReadPermissions =
    session.status === "authenticated" &&
    hasPermission(session.user, "System.Permissions.View");
  const query = useSettingsQuery(
    "settingsEditorSource",
    [kind, id, canReadRoles, canReadPermissions],
    isActive && !completed,
    definition.permission,
    (signal) =>
      fetchEditorSource(kind, id, canReadRoles, canReadPermissions, signal),
  );

  /** 每次实际写入、导出和人工终止都读取最新身份，不能用创建会话时的权限兜底。 */
  function access(): boolean {
    const current = sessionRef.current;
    return (
      current.status === "authenticated" &&
      current.user?.userId === owner.current.userId &&
      current.identityEpoch === owner.current.epoch &&
      (!definition.permission ||
        hasPermission(current.user, definition.permission)) &&
      (!definition.write || hasPermission(current.user, definition.write))
    );
  }
  /** Form 是当前值唯一 owner；归一化比较只保留摘要，原文差异仍受保护。 */
  function report(): void {
    const values =
      loadedRef.current && !completedRef.current
        ? form.getFieldsValue(true)
        : baseline.current;
    const dirty =
      JSON.stringify(projectSettingsValues(kind, values)) !==
      JSON.stringify(projectSettingsValues(kind, baseline.current));
    const rawChanged =
      JSON.stringify(values) !== JSON.stringify(baseline.current);
    summary.current = {
      dirty,
      hasUncommittedInput: !dirty && rawChanged,
      preparing: phaseRef.current === "preparing",
      saving: phaseRef.current === "saving",
      unknown: phaseRef.current === "unknown",
    };
    host.reportSummary(summary.current);
  }
  /** 准备、提交和未知状态同步锁表单与关闭守卫，React 渲染延迟不能放行第二次提交。 */
  function setPhase(next: typeof phase): void {
    phaseRef.current = next;
    setPhaseState(next);
    report();
  }

  useEffect(
    () =>
      host.registerControl({
        getSummary: () => control.current!.getSummary(),
        canClose: () => control.current!.canClose(),
        requestSave: () => control.current!.requestSave(),
        requestDiscard: () => control.current!.requestDiscard(),
      }),
    [host],
  );
  useEffect(() => {
    if (!query.data) return;
    // 后台快照仅更新候选，已经初始化的业务基线和当前值绝不随重连/重新显示改变。
    if (loadedRef.current) {
      setSource((old) => ({
        ...old,
        roles: query.data.roles,
        permissions: query.data.permissions,
      }));
      return;
    }
    baseline.current = initialSettingsValues(kind, query.data);
    form.setFieldsValue(baseline.current);
    sourceRef.current = query.data;
    setSource(query.data);
    loadedRef.current = true;
    setLoaded(true);
  }, [query.data, kind, form]);

  /** 提交前展示就绪、验证字段及完整候选，冻结后只接纳与本次目标和版本匹配的回执。 */
  async function save(): Promise<SaveDecision> {
    if (phaseRef.current === "unknown") return "unknown";
    if (phaseRef.current !== "idle" || !loadedRef.current || !access())
      return "unsaved";
    if (completedRef.current) return "saved";
    setPhase("preparing");
    try {
      await host.ensureVisible();
      const values = await form.validateFields();
      const problem = validateSettingsValues(kind, values, sourceRef.current);
      if (problem) {
        setNotice(problem);
        setPhase("idle");
        return "unsaved";
      }
      if (!access()) {
        setNotice("权限已变化，原输入保留。 ");
        setPhase("idle");
        return "unsaved";
      }
      if (!summary.current.dirty && !summary.current.hasUncommittedInput) {
        setPhase("idle");
        return "saved";
      }
      const version =
        sourceRef.current.userRoles?.version ??
        sourceRef.current.rolePermissions?.version ??
        sourceRef.current.user?.version ??
        sourceRef.current.role?.version ??
        0;
      const frozen = freezeSettingsSubmission(kind, id, version, values);
      attempt.current = frozen;
      recordId.current = null;
      setNotice("");
      setPhase("saving");
      const result = await submitSettings(frozen);
      evidence.current = result;
      if (result.outcome !== "confirmed") {
        const field = result.field
          ? result.field.charAt(0).toLowerCase() + result.field.slice(1)
          : undefined;
        const mapped =
          field === "newPassword"
            ? "password"
            : field === "roleIds" || field === "permissionIds"
              ? "ids"
              : field;
        if (mapped && Object.hasOwn(frozen.values, mapped))
          form.setFields([
            {
              name: mapped as keyof SettingsFormValues,
              errors: [result.error],
            },
          ]);
        setNotice(result.error);
        setPhase(result.outcome === "unknown" ? "unknown" : "idle");
        return result.outcome === "unknown" ? "unknown" : "failed";
      }
      // 已确认回执先接纳原页；随后的列表/me 读取失败不能把成功改写为失败或重复提交。
      const nextSource = structuredClone(sourceRef.current);
      if (kind === "user-profile" || kind === "user-status")
        nextSource.user = result.data as SettingsEditorSource["user"];
      if (kind === "role-profile" || kind === "role-status")
        nextSource.role = result.data as SettingsEditorSource["role"];
      if (kind === "user-roles")
        nextSource.userRoles = result.data as SettingsEditorSource["userRoles"];
      if (kind === "role-permissions")
        nextSource.rolePermissions =
          result.data as SettingsEditorSource["rolePermissions"];
      sourceRef.current = nextSource;
      setSource(nextSource);
      const oneShot =
        kind.endsWith("-create") ||
        kind.endsWith("-status") ||
        kind === "role-delete" ||
        kind.startsWith("password-");
      completedRef.current = oneShot;
      setCompleted(oneShot);
      if (kind.endsWith("-create"))
        setCreatedId(
          typeof result.data === "string"
            ? result.data
            : (result.data as { uid: string }).uid,
        );
      baseline.current =
        kind.startsWith("password-") || kind === "user-create"
          ? projectSettingsValues(kind, {})
          : structuredClone(frozen.values);
      form.resetFields();
      form.setFieldsValue(baseline.current);
      attempt.current = null;
      evidence.current = null;
      setPhase("idle");
      setNotice(
        kind === "password-change"
          ? "密码已修改，旧凭据失效。请在重认证界面重新登录；其他页签草稿保留。"
          : "保存已确认成功。 ",
      );
      await queryClient.invalidateQueries({
        predicate: (entry) =>
          entry.queryKey[0] === "identity" &&
          entry.queryKey[1] === owner.current.epoch &&
          entry.queryKey[2] === owner.current.userId &&
          [
            "getManagedUsersPage",
            "getManagedRolesPage",
            "getManagedPermissions",
          ].includes(String(entry.queryKey[4])),
      });
      if (
        sessionRef.current.status === "authenticated" &&
        sessionRef.current.user?.userId === owner.current.userId
      ) {
        try {
          await sessionRef.current.refreshPermissions();
        } catch {
          if (kind !== "password-change")
            setNotice(
              "保存已确认成功；当前身份核实失败，请重新认证，勿重复提交。 ",
            );
        }
      }
      return isUnprotected(summary.current) ? "saved" : "unsaved";
    } catch (error) {
      if (summary.current.saving) {
        setNotice("提交已发出但未能完成接纳，服务器结果仍需核实。 ");
        setPhase("unknown");
        return "unknown";
      }
      setPhase("idle");
      if (
        typeof error === "object" &&
        error !== null &&
        "errorFields" in error
      ) {
        const fields = error.errorFields as { name: string[] }[];
        if (fields[0]) form.scrollToField(fields[0].name, { focus: true });
        setNotice("字段校验未通过，输入已保留，没有发送保存请求。 ");
      } else
        setNotice(
          error instanceof Error
            ? error.message
            : "保存准备失败，没有发送请求。 ",
        );
      return "unsaved";
    }
  }
  /** 普通放弃只恢复本页基线，未知、准备及提交期间不得释放冻结材料。 */
  async function discard() {
    if (phaseRef.current !== "idle") return "blocked" as const;
    form.resetFields();
    form.setFieldsValue(baseline.current);
    setNotice("");
    report();
    return "discarded" as const;
  }
  /** 凭据字段不能进入文件；恢复导出只保留必要非凭据材料与明确未确认事项。 */
  function exportRecovery(): void {
    if (phaseRef.current !== "unknown" || !attempt.current || !access()) {
      void message.warning("当前权限不允许导出，需现有合法业务/运维渠道介入。");
      return;
    }
    recordId.current = null;
    let url: string | undefined;
    try {
      const record = crypto.randomUUID();
      const content = settingsRecoveryRecord(
        record,
        attempt.current,
        evidence.current ?? {
          outcome: "unknown",
          error: "服务器结果仍未确认。",
        },
      );
      url = URL.createObjectURL(
        new Blob([JSON.stringify(content, null, 2)], {
          type: "application/json;charset=utf-8",
        }),
      );
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `SwiftExport-恢复记录-${record}.json`;
      document.body.append(anchor);
      try {
        anchor.click();
      } finally {
        anchor.remove();
      }
      recordId.current = record;
      void message.info(
        `已发起下载，记录标识：${record}。请确认文件妥善保留；程序不能证明保存。`,
      );
    } catch {
      void message.error("恢复记录导出失败，继续保留恢复限制。");
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }
  /** 只有合法导出后两次主动确认才可人工终止，批量关闭不能调用此动作。 */
  async function terminateRecovery(): Promise<void> {
    const record = recordId.current;
    const allowed = () =>
      phaseRef.current === "unknown" && access() && recordId.current === record;
    if (!record || !allowed()) {
      void message.warning("请先合法导出并保全记录；不能终止本地恢复。");
      return;
    }
    if (
      !(await modal.confirm({
        title: "确认记录已妥善保留",
        content: `请确认已保留 SwiftExport-恢复记录-${record}.json，下载提示不能证明文件保存。`,
        okText: "已妥善保留",
        cancelText: "取消",
      })) ||
      !allowed()
    )
      return;
    if (
      (await modal.confirm({
        title: "确认终止本地恢复",
        content:
          "终止本地恢复会释放本地草稿，服务器结果仍未确认，不能据此认定失败、回滚或安全重发。",
        okText: "终止并关闭",
        cancelText: "取消",
        okButtonProps: { danger: true },
      })) &&
      allowed() &&
      !host.closeAfterManualTermination(record)
    )
      void message.warning("宿主未接受人工终止，恢复限制仍保留。");
  }
  control.current = {
    getSummary: () => summary.current,
    canClose: () => ({
      allowed: isUnprotected(summary.current),
      reason: "请先处理当前输入或未知结果。",
    }),
    requestSave: save,
    requestDiscard: discard,
  };

  const write = access();
  const nativeReadOnly =
    source.role?.isSystem ||
    source.rolePermissions?.isAutomatic ||
    (kind === "password-reset" &&
      (source.user?.isAdmin || !source.user?.isActive));
  const candidatesMissing =
    (kind === "user-roles" && !canReadRoles) ||
    (kind === "role-permissions" && !canReadPermissions);
  const disabled =
    !loaded ||
    !write ||
    phase !== "idle" ||
    completed ||
    !!nativeReadOnly ||
    candidatesMissing;
  const ids = Form.useWatch("ids", form) ?? [];
  const choices =
    kind === "user-roles"
      ? (source.roles ?? []).map((row) => ({
          value: row.uid!,
          label: `${row.roleName} · ${row.roleCode}${row.isActive ? "" : "（停用）"}`,
          disabled: !row.isActive && !ids.includes(row.uid!),
        }))
      : (source.permissions ?? []).map((row) => ({
          value: row.uid!,
          label: `${row.groupKey} · ${row.displayName} (${row.permissionKey})${row.isAssignable ? "" : "（保留）"}`,
          disabled:
            (!row.isActive || !row.isAssignable) && !ids.includes(row.uid!),
        }));
  for (const selected of source.userRoles?.roleIds ??
    source.rolePermissions?.permissionIds ??
    [])
    if (!choices.some((choice) => choice.value === selected))
      choices.push({
        value: selected,
        label: `${selected}（当前关系，候选不可用）`,
        disabled: !ids.includes(selected),
      });
  const required = [
    { required: true, whitespace: true, message: "此项不能为空。" },
  ];
  const passwordFields = kind === "user-create" || kind.startsWith("password-");
  return renderDisplay(
    <Card
      title={definition.title}
      className="settings-editor"
      data-testid={`settings-editor-${kind}`}
      extra={
        <Typography.Text type="secondary">
          {completed ? "保存已确认" : `保存状态：${phase}`}
        </Typography.Text>
      }
    >
      {query.error && (
        <Alert
          type="error"
          showIcon
          title={describeSettingsReadError(query.error)}
          action={
            <Button size="small" onClick={() => void query.refetch()}>
              重试读取
            </Button>
          }
        />
      )}
      {(source.user || source.role) && (
        <Descriptions
          size="small"
          column={2}
          items={[
            {
              key: "target",
              label: "当前目标",
              children: source.user?.userName ?? source.role?.roleName,
            },
            {
              key: "version",
              label: "基线版本",
              children:
                source.userRoles?.version ??
                source.rolePermissions?.version ??
                source.user?.version ??
                source.role?.version,
            },
          ]}
        />
      )}
      {!!nativeReadOnly && (
        <Alert
          type="info"
          title={
            kind === "password-reset"
              ? "管理员密码只能由本人修改；停用账号不能重置。"
              : "内置角色只读，管理员自动拥有全部目录权限。"
          }
        />
      )}
      {candidatesMissing && (
        <Alert
          type="warning"
          title="没有候选目录读取权限，保留已有关系并禁止对应编辑；不会自动扩大权限。"
        />
      )}
      {!write && (
        <Alert type="warning" title="当前会话没有此操作权限，原有输入保留。" />
      )}
      <Form
        name={`settings-${descriptor.tabId}`}
        form={form}
        layout="vertical"
        initialValues={{}}
        disabled={disabled}
        onValuesChange={() => {
          report();
          setNotice("");
        }}
        preserve
      >
        {kind === "user-create" && (
          <Form.Item name="userName" label="登录名" rules={required}>
            <Input autoComplete="off" />
          </Form.Item>
        )}
        {(kind === "user-create" || kind === "user-profile") && (
          <>
            <Form.Item
              name="nickName"
              label="显示名称"
              rules={kind === "user-profile" ? required : undefined}
            >
              <Input />
            </Form.Item>
            <Form.Item
              name="email"
              label="邮箱"
              rules={[{ type: "email", message: "请输入有效邮箱。" }]}
            >
              <Input />
            </Form.Item>
            <Form.Item name="phoneNumber" label="联系电话">
              <Input />
            </Form.Item>
          </>
        )}
        {kind === "role-create" && (
          <Form.Item
            name="roleCode"
            label="角色编码"
            rules={[
              ...required,
              {
                pattern: /^[A-Z][A-Z0-9_]{2,63}$/,
                message: "3—64 位大写字母、数字或下划线，以字母开头。",
              },
              {
                validator: (_, value) =>
                  value === "SYSTEM_ADMIN"
                    ? Promise.reject(new Error("不能使用内置角色编码。"))
                    : Promise.resolve(),
              },
            ]}
          >
            <Input />
          </Form.Item>
        )}
        {kind === "role-profile" && (
          <Typography.Paragraph type="secondary">
            角色编码：{source.role?.roleCode}（创建后不可修改）
          </Typography.Paragraph>
        )}
        {(kind === "role-create" || kind === "role-profile") && (
          <>
            <Form.Item name="roleName" label="角色名称" rules={required}>
              <Input />
            </Form.Item>
            <Form.Item name="description" label="角色描述">
              <Input.TextArea rows={3} />
            </Form.Item>
          </>
        )}
        {(kind === "user-roles" || kind === "role-permissions") && (
          <>
            <Form.Item
              name="ids"
              label={
                kind === "user-roles" ? "角色（完整集合）" : "权限（完整集合）"
              }
            >
              <Select
                mode="multiple"
                showSearch
                allowClear
                optionFilterProp="label"
                options={choices}
                placeholder="搜索并选择；清空表示撤销全部关系"
              />
            </Form.Item>
            <Typography.Paragraph type="secondary">
              搜索只过滤候选显示，保存提交完整选择；停用、缺失或保留关系由你明确移除，不自动删选值。
            </Typography.Paragraph>
          </>
        )}
        {kind.endsWith("-status") && (
          <Form.Item name="isActive" label="启用状态" valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
        {kind === "role-delete" && (
          <Form.Item name="confirmed" valuePropName="checked">
            <Checkbox>确认删除此角色；有用户关联时服务器会拒绝。</Checkbox>
          </Form.Item>
        )}
        {kind === "password-change" && (
          <Form.Item name="currentPassword" label="旧密码" rules={required}>
            <Input.Password autoComplete="current-password" />
          </Form.Item>
        )}
        {passwordFields && (
          <>
            <Form.Item
              name="password"
              label={kind === "user-create" ? "密码" : "新密码"}
              rules={[...required, { min: 6, message: "密码不能少于 6 位。" }]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            <Form.Item
              name="confirmPassword"
              label="确认密码"
              dependencies={["password"]}
              rules={[
                ...required,
                ({ getFieldValue }) => ({
                  validator: (_, value) =>
                    value === getFieldValue("password")
                      ? Promise.resolve()
                      : Promise.reject(new Error("两次密码不一致。")),
                }),
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
          </>
        )}
      </Form>
      {notice && (
        <Alert
          showIcon
          type={phase === "unknown" ? "error" : completed ? "success" : "info"}
          title={notice}
        />
      )}
      <Space wrap style={{ marginTop: 12 }}>
        <Button
          type="primary"
          aria-label={kind === "role-delete" ? "确认删除角色" : "保存"}
          danger={kind === "role-delete"}
          disabled={disabled}
          loading={phase === "preparing" || phase === "saving"}
          onClick={() => void save()}
        >
          {kind === "role-delete" ? "确认删除角色" : "保存"}
        </Button>
        <Button
          disabled={!loaded || phase !== "idle" || completed}
          onClick={async () => {
            if (
              await modal.confirm({
                title: "放弃当前修改？",
                content: "恢复本页原基线，其他页签不受影响。",
                okText: "放弃",
                cancelText: "取消",
              })
            )
              await discard();
          }}
        >
          放弃修改
        </Button>
        {createdId && (
          <Button
            onClick={() =>
              host.open({
                pageKind:
                  kind === "user-create" ? "user-profile" : "role-profile",
                params: { id: createdId },
              })
            }
          >
            打开已创建记录
          </Button>
        )}
      </Space>
      {source.effective && (
        <>
          <Typography.Title level={5}>当前有效权限（只读）</Typography.Title>
          <Space wrap className="settings-permission-tags">
            {source.effective.map((key) => (
              <Tag key={key}>{key}</Tag>
            ))}
          </Space>
          <Typography.Paragraph type="secondary">
            这是读取时的权限投影；保存后以重新打开的读取结果为准。
          </Typography.Paragraph>
        </>
      )}
      {phase === "unknown" && (
        <RecoveryActions
          title="保存结果未知 · 恢复处理"
          description="冻结材料仍保留，保存、普通放弃和批量关闭受限。当前端点没有命令结果查询，不能自动重发；请保全记录并通过现有合法业务/运维渠道核实。密码不进入导出文件。"
          onExport={exportRecovery}
          onTerminate={() => void terminateRecovery()}
        />
      )}
    </Card>,
  );
}
