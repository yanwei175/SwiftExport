import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import type { ReactNode } from "react";
import {
  Alert,
  App,
  Button,
  Card,
  Form,
  Input,
  Select,
  Space,
  Table,
  Typography,
} from "antd";
import type { TableColumnsType } from "antd";
import {
  acceptEditableRow,
  createEditableRow,
  discardEditableRows,
  isRowDirty,
  updateEditableRow,
} from "../../shared/editing/编辑集合";
import type { EditableRow } from "../../shared/editing/编辑集合";
import { useSession } from "../../shared/session/会话上下文";
import { useWorkspaceTab } from "../../shared/workspace/useWorkspaceTab";
import { isUnprotected } from "../../shared/workspace/页签契约";
import type {
  DiscardDecision,
  ProtectionSummary,
  SaveDecision,
  TabControl,
  WorkspaceTabProps,
} from "../../shared/workspace/页签契约";
import { bufferNeedsProtection, parseQuantity } from "./model/单元格缓冲";
import type { CellBuffer } from "./model/单元格缓冲";
import {
  createRecoveryRecord,
  freezeAttempt,
  isCompleteReceipt,
  simulateSave,
} from "./model/模拟保存";
import type {
  FrozenAttempt,
  SaveScenario,
  SimulationResult,
  VerificationValue,
} from "./model/模拟保存";

type Phase = "idle" | "preparing" | "saving" | "unknown";
type FormValue = { name: string; quantity: string };
type Notice = { type: "success" | "warning" | "error" | "info"; text: string };
const initialValue: VerificationValue = { name: "初始名称", quantity: 1 };
const initialFormValue: FormValue = { name: "初始名称", quantity: "1" };
const scenarios: { value: SaveScenario; label: string }[] = [
  { value: "success", label: "完整成功" },
  { value: "not-effective", label: "确认未生效" },
  { value: "business-unknown", label: "业务失败无法确认" },
  { value: "lost-response", label: "响应丢失" },
  { value: "partial", label: "行级部分成功" },
  { value: "slow", label: "慢成功" },
];

/** 展示订阅只存在于 Activity 内，清理计数用于实际浏览器验证隐藏生命周期。 */
function DisplayLifecycle({
  children,
  onEffect,
}: {
  children: ReactNode;
  onEffect: (attached: boolean) => void;
}) {
  useLayoutEffect(() => {
    onEffect(true);
    return () => onEffect(false);
  }, [onEffect]);
  return <>{children}</>;
}

/** 两个受环境开关限制的骨架验收页，共享既定保存契约但从不请求业务写入。 */
export default function VerificationTab({
  descriptor,
  isActive,
  renderDisplay,
}: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const { modal } = App.useApp();
  const [form] = Form.useForm<FormValue>();
  const isGrid = descriptor.pageKind === "verification-grid";
  const [, setRevision] = useState(0);
  const baselineRef = useRef<VerificationValue>({ ...initialValue });
  const formConnectedRef = useRef(false);
  const formUidRef = useRef(descriptor.params.id ?? descriptor.identity);
  const newDocumentRef = useRef(!isGrid && Boolean(descriptor.params.draft));
  const formVersionRef = useRef(1);
  const rowsRef = useRef<EditableRow<VerificationValue>[]>([
    createEditableRow("row-1", `${descriptor.identity}:1`, 1, initialValue),
    createEditableRow("row-2", `${descriptor.identity}:2`, 1, initialValue),
  ]);
  const buffersRef = useRef(new Map<string, CellBuffer>());
  const errorsRef = useRef(new Map<string, string>());
  const phaseRef = useRef<Phase>("idle");
  const scenarioRef = useRef<SaveScenario>("success");
  const attemptsRef = useRef(0);
  const frozenRef = useRef<FrozenAttempt | null>(null);
  const responseRef = useRef<SimulationResult | null>(null);
  const recordRef = useRef<{
    recordId: string;
    identityEpoch: number;
    userId: string;
  } | null>(null);
  const noticeRef = useRef<Notice | null>(null);
  const effectsRef = useRef({ attached: 0, cleaned: 0 });
  const aliveRef = useRef(true);
  const latestRef = useRef<TabControl | null>(null);
  const canRead =
    session.status === "authenticated" &&
    !!session.user?.permissions?.includes("Products.Product.View");
  const canEdit =
    canRead && !!session.user?.permissions?.includes("Products.Product.Edit");
  const locked = phaseRef.current !== "idle";

  /** Form 独占字段当前值；此投影只用于比较/冻结，不另存 React 当前值副本。 */
  function readForm(): FormValue {
    // 首次展示前尚无输入，只比较初始基线；避免读取未连接的 Ant Form 实例。
    if (!formConnectedRef.current) return { ...initialFormValue };
    const values = form.getFieldsValue(true);
    return {
      name: values.name ?? initialFormValue.name,
      quantity: values.quantity ?? initialFormValue.quantity,
    };
  }

  /** 所有入口读取同一最新会话摘要，包括未进入编辑集合的原始缓冲。 */
  function getSummary(): ProtectionSummary {
    const current = isGrid ? null : readForm();
    const dirty = isGrid
      ? rowsRef.current.some(isRowDirty)
      : current!.name !== baselineRef.current.name ||
        parseQuantity(current!.quantity) !== baselineRef.current.quantity;
    return {
      dirty,
      hasUncommittedInput:
        isGrid && [...buffersRef.current.values()].some(bufferNeedsProtection),
      preparing: phaseRef.current === "preparing",
      saving: phaseRef.current === "saving",
      unknown: phaseRef.current === "unknown",
    };
  }

  /** 状态变化同步报告宿主，避免用户立即关闭时读到上一次 render 的 dirty。 */
  function publish() {
    host.reportSummary(getSummary());
    setRevision((value) => value + 1);
  }

  /** 错误反馈属于原页会话，隐藏时只更新摘要，不打断正在编辑的另一个标签。 */
  function showNotice(type: Notice["type"], text: string) {
    noticeRef.current = { type, text };
    publish();
  }

  /** 当前身份与读写权同时满足才可操作此尝试，不以旧权限快照绕过撤销。 */
  function hasCurrentAccess(attempt?: FrozenAttempt): boolean {
    const current = sessionRef.current;
    return (
      current.status === "authenticated" &&
      !!current.user?.userId &&
      !!current.user.permissions?.includes("Products.Product.View") &&
      !!current.user.permissions?.includes("Products.Product.Edit") &&
      (!attempt ||
        (current.identityEpoch === attempt.identityEpoch &&
          current.user.userId === attempt.userId))
    );
  }

  /** 仅把可转换的缓冲写入集合；无效内容始终保留，不因失焦或保存失败丢失。 */
  function commitBuffers(): boolean {
    errorsRef.current.clear();
    for (const [key, buffer] of buffersRef.current) {
      if (buffer.field === "quantity") {
        const quantity = parseQuantity(buffer.raw);
        if (quantity === null)
          errorsRef.current.set(key, "数量必须是正整数，未完成输入仍保留。 ");
      } else if (!buffer.raw.trim())
        errorsRef.current.set(key, "名称不能为空。 ");
    }
    if (errorsRef.current.size) return false;
    for (const buffer of buffersRef.current.values()) {
      rowsRef.current = updateEditableRow(
        rowsRef.current,
        buffer.clientKey,
        buffer.field === "name"
          ? { name: buffer.raw }
          : { quantity: parseQuantity(buffer.raw)! },
      );
    }
    buffersRef.current.clear();
    return true;
  }

  /** 保存准备到接纳持有同一会话锁；确定未发送才可解除准备锁并保留输入。 */
  async function requestSave(): Promise<SaveDecision> {
    if (phaseRef.current !== "idle")
      return phaseRef.current === "unknown" ? "unknown" : "failed";
    if (!hasCurrentAccess()) {
      showNotice("warning", "当前身份没有合法编辑权限，未发送提交。 ");
      return "failed";
    }
    phaseRef.current = "preparing";
    recordRef.current = null;
    publish();
    try {
      await host.ensureVisible();
      if (!aliveRef.current) return "unknown";
      if (!hasCurrentAccess())
        throw new Error("准备期间权限改变，未发送提交。 ");
      let items;
      if (isGrid) {
        if (!commitBuffers())
          throw new Error("单元格输入未通过校验，未发送提交。 ");
        if (
          rowsRef.current.some(
            (row) =>
              !row.current.name.trim() ||
              parseQuantity(String(row.current.quantity)) === null,
          )
        ) {
          throw new Error("编辑集合校验失败，未发送提交。 ");
        }
        items = rowsRef.current.map((row) => ({
          ...row.current,
          clientKey: row.clientKey,
          uid: row.uid,
          version: row.version,
        }));
      } else {
        await form.validateFields();
        const current = readForm();
        items = [
          {
            name: current.name,
            quantity: parseQuantity(current.quantity)!,
            clientKey: "form-1",
            uid: formUidRef.current,
            version: formVersionRef.current,
          },
        ];
      }
      const current = sessionRef.current;
      if (!hasCurrentAccess()) throw new Error("冻结前权限改变，未发送提交。 ");
      const attempt = freezeAttempt(
        descriptor.tabId,
        current.identityEpoch,
        current.user!.userId!,
        items,
        newDocumentRef.current,
      );
      frozenRef.current = attempt;
      responseRef.current = null;
      phaseRef.current = "saving";
      attemptsRef.current += 1;
      publish();
      const result = await simulateSave(attempt, scenarioRef.current);
      if (
        !aliveRef.current ||
        sessionRef.current.identityEpoch !== attempt.identityEpoch ||
        sessionRef.current.user?.userId !== attempt.userId
      ) {
        // 换身份后拒绝旧回执写入，宿主随后销毁旧会话；取消接收不宣称回滚。
        return "unknown";
      }
      responseRef.current = result;
      if (result.kind === "unknown") {
        phaseRef.current = "unknown";
        showNotice(
          "error",
          `${result.explanation} 保留冻结请求与锁，禁止自动重发或普通放弃。`,
        );
        return "unknown";
      }
      if (result.kind === "not-effective") {
        phaseRef.current = "idle";
        showNotice("warning", result.explanation);
        return "failed";
      }
      if (!isCompleteReceipt(attempt, result)) {
        phaseRef.current = "unknown";
        showNotice(
          "error",
          "模拟回执不完整或无法关联，保持结果未知及恢复限制。 ",
        );
        return "unknown";
      }
      for (const receipt of result.items) {
        if (!receipt.success) continue;
        const submitted = attempt.items.find(
          (item) => item.clientKey === receipt.clientKey,
        )!;
        if (isGrid)
          rowsRef.current = acceptEditableRow(
            rowsRef.current,
            receipt.clientKey,
            receipt.uid,
            receipt.version,
            { name: submitted.name, quantity: submitted.quantity },
          );
        else {
          baselineRef.current = {
            name: submitted.name,
            quantity: submitted.quantity,
          };
          formVersionRef.current = receipt.version;
          formUidRef.current = receipt.uid;
        }
      }
      phaseRef.current = "idle";
      const partial = result.items.some((item) => !item.success);
      const identityAccepted =
        !partial && newDocumentRef.current
          ? host.updateBusinessIdentity(result.items[0]!.uid)
          : true;
      if (!partial) newDocumentRef.current = false;
      showNotice(
        partial || !identityAccepted ? "warning" : "success",
        partial
          ? "行级回执已核对，仅接纳成功行；明确失败行草稿保留。"
          : !identityAccepted
            ? `完整回执已接纳，实体 Uid：${formUidRef.current}；定位更新未完成，保留原实例，请核对后处理。`
            : "完整模拟回执已核对并接纳；没有业务库写入。 ",
      );
      return partial || !isUnprotected(getSummary()) ? "unsaved" : "saved";
    } catch (error) {
      if (!aliveRef.current) return "unknown";
      if (phaseRef.current === "saving") {
        phaseRef.current = "unknown";
        responseRef.current = {
          kind: "unknown",
          code: "SIMULATION_RESPONSE_LOST",
          explanation: "派发后本地接收异常，不能证明未生效。",
        };
        showNotice("error", "派发后结果无法确认，保持冻结请求与锁。 ");
        return "unknown";
      }
      phaseRef.current = "idle";
      const explanation =
        error instanceof Error
          ? error.message
          : "字段校验失败，输入保留，未发送提交。";
      showNotice("error", explanation);
      return "failed";
    }
  }

  /** 放弃只处理可放弃的业务值和缓冲；未知及提交阶段均拒绝普通放弃。 */
  async function requestDiscard(): Promise<DiscardDecision> {
    const current = sessionRef.current;
    // 本地放弃只要求当前读取权限；写权撤销不能冒充写入，也不能隐式丢草稿。
    if (
      phaseRef.current !== "idle" ||
      current.status !== "authenticated" ||
      !current.user?.permissions?.includes("Products.Product.View")
    )
      return "blocked";
    if (isGrid) rowsRef.current = discardEditableRows(rowsRef.current);
    else
      form.setFieldsValue({
        name: baselineRef.current.name,
        quantity: String(baselineRef.current.quantity),
      });
    buffersRef.current.clear();
    errorsRef.current.clear();
    noticeRef.current = null;
    publish();
    return "discarded";
  }

  latestRef.current = {
    getSummary,
    canClose: () =>
      isUnprotected(getSummary())
        ? { allowed: true }
        : {
            allowed: false,
            reason: "本页仍有业务修改、未提交输入或保存/未知状态。",
          },
    requestSave,
    requestDiscard,
  };

  useEffect(
    () =>
      host.registerControl({
        getSummary: () => latestRef.current!.getSummary(),
        canClose: () => latestRef.current!.canClose(),
        requestSave: () => latestRef.current!.requestSave(),
        requestDiscard: () => latestRef.current!.requestDiscard(),
      }),
    [host],
  );

  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
    };
  }, []);

  useEffect(() => {
    host.reportSummary(latestRef.current!.getSummary());
  }, [host, canRead, canEdit, session.status]);

  const onDisplayEffect = useCallback(
    (attached: boolean) => {
      if (attached) {
        if (!isGrid) formConnectedRef.current = true;
        effectsRef.current.attached += 1;
      } else effectsRef.current.cleaned += 1;
      setRevision((value) => value + 1);
    },
    [isGrid],
  );

  /** 控件原始输入变化立即受保护；切换导致的 blur 不自动提交缓冲。 */
  function changeCell(
    row: EditableRow<VerificationValue>,
    field: keyof VerificationValue,
    raw: string,
  ) {
    if (phaseRef.current !== "idle" || !hasCurrentAccess()) return;
    const key = `${row.clientKey}:${field}`;
    const existing = buffersRef.current.get(key);
    const buffer: CellBuffer = {
      clientKey: row.clientKey,
      field,
      raw,
      original: existing?.original ?? row.current[field],
    };
    if (bufferNeedsProtection(buffer)) buffersRef.current.set(key, buffer);
    else buffersRef.current.delete(key);
    errorsRef.current.delete(key);
    publish();
  }

  /** 明确取消仅解除对应单元格缓冲，不抹除已提交到集合的其他修改。 */
  function cancelCell(key?: string) {
    if (phaseRef.current !== "idle") return;
    if (key) {
      buffersRef.current.delete(key);
      errorsRef.current.delete(key);
    } else {
      buffersRef.current.clear();
      errorsRef.current.clear();
    }
    publish();
  }

  /** 数据替换复用最新保护语义及同一保存/放弃入口，用户取消时保持输入。 */
  async function requestReplace() {
    if (phaseRef.current !== "idle") {
      showNotice("warning", "保存或未知状态禁止替换编辑数据。 ");
      return;
    }
    if (!latestRef.current!.canClose().allowed) {
      const accepted = await modal.confirm({
        title: "替换数据前处理未保存输入",
        content: "刷新将恢复本地基线；确认放弃本页修改和未提交输入？",
        okText: "放弃并刷新",
        cancelText: "取消",
      });
      if (
        !accepted ||
        (await latestRef.current!.requestDiscard()) !== "discarded"
      )
        return;
    }
    if (!latestRef.current!.canClose().allowed) return;
    showNotice("info", "刷新完成；仅重新呈现当前基线，没有请求业务库。 ");
  }

  /** 仅下载白名单恢复材料，下载启动不作为磁盘可靠保存的证明。 */
  function exportRecovery() {
    const attempt = frozenRef.current;
    const response = responseRef.current;
    if (
      phaseRef.current !== "unknown" ||
      !attempt ||
      !response ||
      !hasCurrentAccess(attempt)
    ) {
      showNotice(
        "warning",
        "当前身份或业务权限不允许保全该恢复材料；需现有合法业务/运维渠道介入。 ",
      );
      return;
    }
    recordRef.current = null;
    const recordId = crypto.randomUUID();
    let url: string | undefined;
    try {
      const record = createRecoveryRecord(recordId, attempt, response);
      url = URL.createObjectURL(
        new Blob([JSON.stringify(record, null, 2)], {
          type: "application/json;charset=utf-8",
        }),
      );
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `SwiftExport-恢复记录-${recordId}.json`;
      document.body.append(anchor);
      try {
        anchor.click();
      } finally {
        anchor.remove();
      }
      recordRef.current = {
        recordId,
        identityEpoch: attempt.identityEpoch,
        userId: attempt.userId,
      };
      showNotice(
        "info",
        `已发起下载，记录标识：${recordId}。程序无法确认文件已经保存；请妥善保留后再执行人工终止。`,
      );
    } catch {
      showNotice("error", "恢复记录导出失败，未完成保全，继续保留恢复限制。 ");
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }

  /** 专用人工终止需要匹配保全标识及两步确认，普通关闭和批量关闭不会调用。 */
  async function terminateRecovery() {
    const attempt = frozenRef.current;
    const record = recordRef.current;
    if (
      phaseRef.current !== "unknown" ||
      !attempt ||
      !record ||
      !hasCurrentAccess(attempt) ||
      record.identityEpoch !== attempt.identityEpoch ||
      record.userId !== attempt.userId
    ) {
      showNotice(
        "warning",
        "尚未完成合法恢复记录导出，不能终止；需现有合法业务/运维渠道介入。 ",
      );
      return;
    }
    const retained = await modal.confirm({
      title: "确认记录已妥善保留",
      content: `请确认已妥善保留 SwiftExport-恢复记录-${record.recordId}.json，记录标识：${record.recordId}。浏览器下载提示本身不能证明文件保存。`,
      okText: "已妥善保留",
      cancelText: "取消",
    });
    if (
      !retained ||
      !hasCurrentAccess(attempt) ||
      phaseRef.current !== "unknown"
    )
      return;
    const terminated = await modal.confirm({
      title: "确认终止本地恢复",
      content:
        "终止本地恢复会释放本地草稿，服务器结果仍未确认，不能据此认定失败、回滚或安全重发。",
      okText: "终止并关闭",
      cancelText: "取消",
      okButtonProps: { danger: true },
    });
    if (
      !terminated ||
      !hasCurrentAccess(attempt) ||
      phaseRef.current !== "unknown"
    )
      return;
    if (!host.closeAfterManualTermination(record.recordId))
      showNotice("warning", "宿主未接受人工终止，恢复材料继续保留。 ");
  }

  const summary = getSummary();
  const columns: TableColumnsType<EditableRow<VerificationValue>> = [
    { title: "行", dataIndex: "clientKey", width: 100 },
    ...(["name", "quantity"] as const).map((field) => ({
      title: field === "name" ? "名称" : "数量",
      key: field,
      render: (
        _: unknown,
        row: EditableRow<VerificationValue>,
        index: number,
      ) => {
        const key = `${row.clientKey}:${field}`;
        const error = errorsRef.current.get(key);
        return (
          <div>
            <Input
              aria-label={`${index === 0 ? "行" : `第${index + 1}行`}${field === "name" ? "名称" : "数量"}`}
              value={
                buffersRef.current.get(key)?.raw ?? String(row.current[field])
              }
              status={error ? "error" : undefined}
              readOnly={locked || !canEdit}
              onChange={(event) => changeCell(row, field, event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  cancelCell(key);
                }
              }}
            />
            {error && (
              <Typography.Text type="danger" role="alert">
                {error}
              </Typography.Text>
            )}
          </div>
        );
      },
    })),
    { title: "基线版本", dataIndex: "version", width: 120 },
  ];

  return renderDisplay(
    <DisplayLifecycle onEffect={onDisplayEffect}>
      <Space
        orientation="vertical"
        size="middle"
        style={{ width: "100%" }}
        data-testid={`verification-${descriptor.tabId}`}
      >
        <Alert
          type="info"
          title="骨架验收，不写业务库"
          description="本页只验证输入、保存、回执和恢复生命周期；所有提交均为明确的本地模拟，没有真实 Product 请求。"
        />
        <Card
          title={
            isGrid ? "编辑集合与未提交单元格验收" : "Ant Form 字段所有权验收"
          }
        >
          <Space orientation="vertical" size="middle" style={{ width: "100%" }}>
            <Typography.Text>
              标签身份：{descriptor.tabId}；业务身份：{descriptor.identity}
            </Typography.Text>
            <Typography.Text>
              是否活动：{isActive ? "是" : "否"}；展示副作用建立：
              {effectsRef.current.attached}；展示副作用清理：
              {effectsRef.current.cleaned}
            </Typography.Text>
            <Typography.Text>
              业务修改：{summary.dirty ? "是" : "否"}；未提交输入：
              {summary.hasUncommittedInput ? "是" : "否"}
            </Typography.Text>
            <Typography.Text>
              保存状态：
              {
                {
                  idle: "就绪",
                  preparing: "准备中",
                  saving: "保存中",
                  unknown: "结果未知",
                }[phaseRef.current]
              }
              ；提交次数：{attemptsRef.current}；回执版本：
              {isGrid
                ? Math.max(...rowsRef.current.map((row) => row.version))
                : formVersionRef.current}
            </Typography.Text>
            {!canEdit && (
              <Alert
                type="warning"
                title="当前没有编辑权限；已有输入保留，禁止保存和恢复记录导出。"
              />
            )}
            {isGrid ? (
              <Table
                rowKey="clientKey"
                dataSource={rowsRef.current}
                columns={columns}
                pagination={false}
                size="small"
              />
            ) : (
              <Form<FormValue>
                name={`verification-${descriptor.tabId}`}
                form={form}
                initialValues={initialFormValue}
                layout="vertical"
                preserve
                disabled={locked || !canEdit}
                onValuesChange={publish}
              >
                <Form.Item
                  name="name"
                  label="名称"
                  rules={[
                    {
                      required: true,
                      whitespace: true,
                      message: "名称不能为空。",
                    },
                  ]}
                >
                  <Input />
                </Form.Item>
                <Form.Item
                  name="quantity"
                  label="数量"
                  rules={[
                    {
                      validator: async (_, value: string) => {
                        if (parseQuantity(value) === null)
                          throw new Error("数量必须是正整数。");
                      },
                    },
                  ]}
                >
                  <Input inputMode="numeric" />
                </Form.Item>
              </Form>
            )}
            <Space wrap>
              <Select
                aria-label="保存场景"
                value={scenarioRef.current}
                options={scenarios}
                disabled={locked}
                style={{ width: 190 }}
                onChange={(value) => {
                  scenarioRef.current = value;
                  publish();
                }}
              />
              <Button
                type="primary"
                disabled={locked || !canEdit}
                onClick={() => void requestSave()}
              >
                保存
              </Button>
              <Button
                disabled={locked}
                onClick={async () => {
                  const accepted = await modal.confirm({
                    title: "放弃本页修改？",
                    content: "恢复基线并清除本页尚未提交的单元格输入。",
                    okText: "放弃",
                    cancelText: "取消",
                  });
                  if (accepted) await latestRef.current!.requestDiscard();
                }}
              >
                放弃修改
              </Button>
              {isGrid && (
                <Button disabled={locked} onClick={() => cancelCell()}>
                  取消单元格编辑
                </Button>
              )}
              <Button onClick={() => void requestReplace()}>刷新数据</Button>
            </Space>
            {noticeRef.current && (
              <Alert
                type={noticeRef.current.type}
                title={noticeRef.current.text}
                showIcon
                role="status"
              />
            )}
            {phaseRef.current === "unknown" && (
              <Card size="small" title="未知结果恢复（本地模拟）">
                <Space orientation="vertical">
                  <Typography.Text>
                    冻结尝试：{frozenRef.current?.attemptId}；attemptId
                    不是后端幂等键。
                  </Typography.Text>
                  <Space>
                    <Button onClick={exportRecovery}>导出恢复记录</Button>
                    <Button danger onClick={() => void terminateRecovery()}>
                      终止本地恢复
                    </Button>
                  </Space>
                </Space>
              </Card>
            )}
          </Space>
        </Card>
      </Space>
    </DisplayLifecycle>,
  );
}
