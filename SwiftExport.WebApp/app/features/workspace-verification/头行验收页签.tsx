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
  DatePicker,
  Form,
  Input,
  Select,
  Space,
  Typography,
} from "antd";
import dayjs from "dayjs";
import type { Dayjs } from "dayjs";
import { PreferenceDataGrid } from "../../shared/ui/偏好表格";
import { ProForm } from "@ant-design/pro-components/form";
import type {
  SwiftDataGridColumn,
  SwiftDataGridFillDownIntent,
  SwiftDataGridRowKey,
} from "../../shared/ui/swift-data-grid";
import { LookupSelect } from "../../shared/ui/候选选择";
import { RecoveryActions } from "../../shared/ui/恢复操作";
import { describeApiError } from "../../shared/api/响应错误";
import {
  acceptEditableRow,
  createEditableRow,
  discardEditableRows,
  isRowDirty,
  updateEditableRow,
} from "../../shared/editing/编辑集合";
import type { EditableRow } from "../../shared/editing/编辑集合";
import { hasInputChanges } from "../../shared/editing/未提交输入";
import type { UncommittedInput } from "../../shared/editing/未提交输入";
import { locateFieldIssues } from "../../shared/editing/字段错误";
import { parseDecimalText } from "../../shared/format/数值转换";
import { useSession } from "../../shared/session/会话上下文";
import { recordDiagnostic } from "../../shared/diagnostics/诊断记录";
import { useWorkspaceTab } from "../../shared/workspace/useWorkspaceTab";
import { isUnprotected } from "../../shared/workspace/页签契约";
import type {
  DiscardDecision,
  ProtectionSummary,
  SaveDecision,
  TabControl,
  WorkspaceTabProps,
} from "../../shared/workspace/页签契约";
import { useCategoryOptions } from "./model/分类候选查询";
import {
  createDocumentRecoveryRecord,
  freezeDocument,
  isCompleteDocumentReceipt,
  parseDocumentLineField,
  projectDocumentHeader,
  simulateDocumentSave,
} from "./model/头行模拟保存";
import type {
  DocumentHeader,
  DocumentLine,
  DocumentResult,
  DocumentScenario,
  FrozenDocument,
} from "./model/头行模拟保存";

type HeaderForm = { name: string; amount: string; businessDate: Dayjs | null };
type LineBuffer = UncommittedInput<
  keyof DocumentLine,
  DocumentLine[keyof DocumentLine]
>;
type Phase = "idle" | "preparing" | "saving" | "unknown";
type Notice = { type: "success" | "warning" | "error" | "info"; text: string };
const initialHeader: DocumentHeader = {
  name: "验收单据",
  amount: null,
  businessDate: null,
};
const initialForm: HeaderForm = {
  name: initialHeader.name,
  amount: "",
  businessDate: null,
};
const initialLine: DocumentLine = {
  name: "验收明细",
  quantity: 1,
  kind: "normal",
  businessDate: null,
  categoryId: null,
};
const scenarioOptions = [
  { value: "success", label: "完整成功" },
  { value: "not-effective", label: "确认未生效" },
  { value: "business-unknown", label: "业务失败无法确认" },
  { value: "lost-response", label: "响应丢失" },
  { value: "slow", label: "慢成功" },
];
const lineFields: readonly (keyof DocumentLine)[] = [
  "name",
  "quantity",
  "kind",
  "businessDate",
  "categoryId",
];
const fieldTitles = {
  name: "名称",
  quantity: "数量",
  kind: "类型",
  businessDate: "日期",
  categoryId: "分类",
};

/** 展示连接位于 Activity 子树，控制器及未提交缓冲始终位于外部编辑会话。 */
function ConnectedForm({
  children,
  connected,
}: {
  children: ReactNode;
  connected(): void;
}) {
  useLayoutEffect(connected, [connected]);
  return <>{children}</>;
}

/** 验收头 Form 与多行集合的同一保存会话；真实 HTTP 仅用于已有分类读取，保存全部本地模拟。 */
export default function DocumentVerificationTab({
  descriptor,
  isActive,
  renderDisplay,
}: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const ownerRef = useRef({
    identityEpoch: session.identityEpoch,
    userId: session.user?.userId,
  });
  const { modal } = App.useApp();
  const [form] = Form.useForm<HeaderForm>();
  const [, revise] = useState(0);
  const connectedRef = useRef(false);
  const baselineRef = useRef({ ...initialHeader });
  const headerVersionRef = useRef(1);
  const rowsRef = useRef<EditableRow<DocumentLine>[]>([
    createEditableRow("doc-row-1", "document-line-1", 1, initialLine),
    createEditableRow("doc-row-2", "document-line-2", 1, initialLine),
  ]);
  const buffersRef = useRef(new Map<string, LineBuffer>());
  const errorsRef = useRef(new Map<string, string>());
  const pageErrorsRef = useRef<string[]>([]);
  const searchRef = useRef(new Map<string, string>());
  const labelsRef = useRef(new Map<string, string>());
  const selectionRef = useRef<readonly SwiftDataGridRowKey[]>([]);
  const phaseRef = useRef<Phase>("idle");
  const scenarioRef = useRef<DocumentScenario>("success");
  const attemptsRef = useRef(0);
  const frozenRef = useRef<FrozenDocument | null>(null);
  const responseRef = useRef<DocumentResult | null>(null);
  const recordRef = useRef<string | null>(null);
  const noticeRef = useRef<Notice | null>(null);
  const aliveRef = useRef(true);
  const controlsRef = useRef<TabControl | null>(null);
  const canRead =
    session.status === "authenticated" &&
    !!session.user?.permissions?.includes("Products.Product.View");
  const canEdit =
    canRead && !!session.user?.permissions?.includes("Products.Product.Edit");
  const canReadCategory =
    canRead && !!session.user?.permissions?.includes("Products.Category.View");
  const categories = useCategoryOptions(
    {
      userId: session.user?.userId ?? "",
      identityEpoch: session.identityEpoch,
      permissionEpoch: session.permissionEpoch,
    },
    isActive && canReadCategory,
  );
  const options = canReadCategory ? (categories.data ?? []) : [];
  const locked = phaseRef.current !== "idle";

  /** 控件未连接时只返回基线投影，连接后 Form 是头字段当前值的唯一来源。 */
  function readHeader() {
    const values = connectedRef.current
      ? form.getFieldsValue(true)
      : initialForm;
    return projectDocumentHeader(
      values.name ?? "",
      values.amount ?? "",
      values.businessDate?.format("YYYY-MM-DD") ?? null,
    );
  }
  /** 每条路径使用同一最新保护摘要，包括不能转换成明细业务值的原始输入。 */
  function getSummary(): ProtectionSummary {
    const header = readHeader();
    return {
      dirty:
        !header.valid ||
        JSON.stringify(header.value) !== JSON.stringify(baselineRef.current) ||
        rowsRef.current.some(isRowDirty),
      hasUncommittedInput: [...buffersRef.current.values()].some((buffer) =>
        hasInputChanges(buffer, (raw) =>
          parseDocumentLineField(buffer.field, raw),
        ),
      ),
      preparing: phaseRef.current === "preparing",
      saving: phaseRef.current === "saving",
      unknown: phaseRef.current === "unknown",
    };
  }
  /** 同步发布避免立即关闭读到过期 render；展示用修订号不保存任何业务字段。 */
  function publish() {
    host.reportSummary(getSummary());
    revise((value) => value + 1);
  }
  function notice(type: Notice["type"], text: string) {
    noticeRef.current = { type, text };
    publish();
  }
  /** 操作前复核原会话身份与当前权限，导出和修改不会绕过撤权。 */
  function currentAccess(write = true): boolean {
    const current = sessionRef.current;
    return (
      current.status === "authenticated" &&
      current.identityEpoch === ownerRef.current.identityEpoch &&
      current.user?.userId === ownerRef.current.userId &&
      !!current.user?.permissions?.includes("Products.Product.View") &&
      (!write || !!current.user.permissions?.includes("Products.Product.Edit"))
    );
  }
  /** 从 feature 缓冲或集合值取得文本，自定义控件不得另建长期业务当前值。 */
  function cellText(
    row: EditableRow<DocumentLine>,
    field: keyof DocumentLine,
  ): string {
    return (
      buffersRef.current.get(`${row.clientKey}:${field}`)?.raw ??
      String(row.current[field] ?? "")
    );
  }
  /** 所有编辑器重新核权；分类候选还要求独立读取权，聚焦和搜索本身不产生 dirty。 */
  function changeCell(
    row: EditableRow<DocumentLine>,
    field: keyof DocumentLine,
    raw: string,
  ) {
    if (
      phaseRef.current !== "idle" ||
      !currentAccess() ||
      (field === "categoryId" &&
        (!sessionRef.current.user?.permissions?.includes(
          "Products.Category.View",
        ) ||
          (raw !== "" && !options.some((option) => option.value === raw))))
    )
      return;
    const key = `${row.clientKey}:${field}`;
    const buffer: LineBuffer = {
      clientKey: row.clientKey,
      field,
      raw,
      original: buffersRef.current.get(key)?.original ?? row.current[field],
    };
    if (hasInputChanges(buffer, (text) => parseDocumentLineField(field, text)))
      buffersRef.current.set(key, buffer);
    else buffersRef.current.delete(key);
    errorsRef.current.delete(key);
    publish();
  }
  /** Escape 只取消对应原始缓冲，已进入集合的其他草稿保持原状。 */
  function cancelCell(
    row: EditableRow<DocumentLine>,
    field: keyof DocumentLine,
  ) {
    if (phaseRef.current !== "idle" || !currentAccess(false)) return;
    const key = `${row.clientKey}:${field}`;
    buffersRef.current.delete(key);
    errorsRef.current.delete(key);
    publish();
  }
  /** 保存前统一校验所有原始缓冲及明细业务值；任何错误均不生成提交尝试。 */
  function commitAndValidateLines(): boolean {
    errorsRef.current.clear();
    for (const [key, buffer] of buffersRef.current) {
      if (!parseDocumentLineField(buffer.field, buffer.raw).valid)
        errorsRef.current.set(
          key,
          `${fieldTitles[buffer.field]}无效或尚未输入完成。`,
        );
      if (
        buffer.field === "categoryId" &&
        !sessionRef.current.user?.permissions?.includes(
          "Products.Category.View",
        )
      )
        errorsRef.current.set(
          key,
          "分类读取权限已撤销，请取消该输入或由合法渠道处理。",
        );
      if (
        buffer.field === "categoryId" &&
        buffer.raw !== "" &&
        !options.some((option) => option.value === buffer.raw)
      )
        errorsRef.current.set(
          key,
          "该分类已不在当前允许候选中；旧标签只用于显示，请重新选择或取消输入。",
        );
    }
    if (errorsRef.current.size) return false;
    for (const buffer of buffersRef.current.values()) {
      const parsed = parseDocumentLineField(buffer.field, buffer.raw);
      if (parsed.valid)
        rowsRef.current = updateEditableRow(rowsRef.current, buffer.clientKey, {
          [buffer.field]: parsed.value,
        });
    }
    buffersRef.current.clear();
    for (const row of rowsRef.current)
      for (const field of lineFields)
        if (
          !parseDocumentLineField(field, String(row.current[field] ?? "")).valid
        )
          errorsRef.current.set(
            `${row.clientKey}:${field}`,
            `${fieldTitles[field]}无效。`,
          );
    return !errorsRef.current.size;
  }
  /** 头校验和行校验共用准备锁，完整冻结后才进入本地模拟派发；未知绝不自动重提。 */
  async function requestSave(): Promise<SaveDecision> {
    if (phaseRef.current !== "idle")
      return phaseRef.current === "unknown" ? "unknown" : "failed";
    if (!currentAccess()) {
      notice("warning", "当前身份或编辑权限不允许保存，未派发。 ");
      return "failed";
    }
    phaseRef.current = "preparing";
    recordRef.current = null;
    publish();
    try {
      await host.ensureVisible();
      if (!aliveRef.current || !currentAccess())
        throw new Error("展示或权限条件失效，未派发保存。 ");
      const linesValid = commitAndValidateLines();
      await form.validateFields();
      const header = readHeader();
      if (!linesValid || !header.valid)
        throw new Error("头或明细校验失败，输入保留，未发送请求。 ");
      if (!currentAccess()) throw new Error("冻结前权限改变，未派发保存。 ");
      const attempt = freezeDocument({
        tabId: descriptor.tabId,
        identityEpoch: sessionRef.current.identityEpoch,
        userId: sessionRef.current.user!.userId!,
        header: {
          ...header.value,
          uid: "document-verification",
          version: headerVersionRef.current,
        },
        lines: rowsRef.current.map((row) => ({
          ...row.current,
          clientKey: row.clientKey,
          uid: row.uid,
          version: row.version,
        })),
      });
      frozenRef.current = attempt;
      responseRef.current = null;
      phaseRef.current = "saving";
      attemptsRef.current += 1;
      publish();
      const result = await simulateDocumentSave(attempt, scenarioRef.current);
      if (
        !aliveRef.current ||
        sessionRef.current.identityEpoch !== attempt.identityEpoch ||
        sessionRef.current.user?.userId !== attempt.userId
      )
        return "unknown";
      responseRef.current = result;
      recordDiagnostic({
        event: "save-outcome",
        level: "info",
        pageKind: descriptor.pageKind,
        tabId: descriptor.tabId,
        operationId: "verification.document.save",
        attemptId: attempt.attemptId,
        outcome:
          result.kind === "not-effective"
            ? "B"
            : result.kind === "confirmed" &&
                isCompleteDocumentReceipt(attempt, result)
              ? "A"
              : "C",
      });
      if (result.kind === "unknown") {
        phaseRef.current = "unknown";
        notice("error", result.explanation);
        return "unknown";
      }
      if (result.kind === "not-effective") {
        phaseRef.current = "idle";
        notice("warning", result.explanation);
        return "failed";
      }
      if (!isCompleteDocumentReceipt(attempt, result)) {
        phaseRef.current = "unknown";
        notice("error", "头行回执不完整或错配，保持结果未知。 ");
        return "unknown";
      }
      baselineRef.current = {
        name: attempt.header.name,
        amount: attempt.header.amount,
        businessDate: attempt.header.businessDate,
      };
      headerVersionRef.current = result.header.version;
      for (const receipt of result.lines) {
        const submitted = attempt.lines.find(
          (line) => line.clientKey === receipt.clientKey,
        )!;
        rowsRef.current = acceptEditableRow(
          rowsRef.current,
          receipt.clientKey,
          receipt.uid,
          receipt.version,
          {
            name: submitted.name,
            quantity: submitted.quantity,
            kind: submitted.kind,
            businessDate: submitted.businessDate,
            categoryId: submitted.categoryId,
          },
        );
      }
      phaseRef.current = "idle";
      notice(
        "success",
        "头与所有明细完整模拟回执已核对接纳，没有业务库写入。 ",
      );
      return isUnprotected(getSummary()) ? "saved" : "unsaved";
    } catch (error) {
      if (!aliveRef.current) return "unknown";
      if (phaseRef.current === "saving") {
        phaseRef.current = "unknown";
        responseRef.current = {
          kind: "unknown",
          code: "SIMULATION_RESPONSE_LOST",
          explanation: "派发后无法接收完整结果。",
        };
        notice("error", "派发后的异常不能证明未生效，继续保留锁。 ");
        return "unknown";
      }
      phaseRef.current = "idle";
      notice(
        "error",
        error instanceof Error
          ? error.message
          : "头字段校验失败，未发送请求，输入保留。 ",
      );
      return "failed";
    }
  }
  /** 普通放弃只在当前读权合法且没有准备/提交/未知锁时恢复同一头行基线。 */
  async function requestDiscard(): Promise<DiscardDecision> {
    if (phaseRef.current !== "idle" || !currentAccess(false)) return "blocked";
    const baseline = baselineRef.current;
    form.setFieldsValue({
      name: baseline.name,
      amount: baseline.amount ?? "",
      businessDate: baseline.businessDate ? dayjs(baseline.businessDate) : null,
    });
    rowsRef.current = discardEditableRows(rowsRef.current);
    buffersRef.current.clear();
    errorsRef.current.clear();
    pageErrorsRef.current = [];
    noticeRef.current = null;
    publish();
    return "discarded";
  }
  controlsRef.current = {
    getSummary,
    canClose: () =>
      isUnprotected(getSummary())
        ? { allowed: true }
        : { allowed: false, reason: "头、明细或未提交输入仍需要保护。" },
    requestSave,
    requestDiscard,
  };
  useEffect(
    () =>
      host.registerControl({
        getSummary: () => controlsRef.current!.getSummary(),
        canClose: () => controlsRef.current!.canClose(),
        requestSave: () => controlsRef.current!.requestSave(),
        requestDiscard: () => controlsRef.current!.requestDiscard(),
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
    host.reportSummary(controlsRef.current!.getSummary());
  }, [host, canRead, canEdit, session.status]);
  const connect = useCallback(() => {
    connectedRef.current = true;
  }, []);

  /** feature 显式声明字段路径，未识别的字段保留页级错误；不靠中文 message 猜测定位。 */
  function simulateFieldErrors() {
    if (locked || !currentAccess(false)) return;
    const mapped = locateFieldIssues(
      [
        { field: "Header.Name", message: "模拟头名称错误" },
        { field: "Lines.doc-row-1.Quantity", message: "模拟明细数量错误" },
        { field: "Unmapped.Field", message: "未识别字段保留页级错误" },
      ],
      (field) =>
        field === "Header.Name"
          ? { kind: "form", name: ["name"] }
          : field === "Lines.doc-row-1.Quantity"
            ? { kind: "cell", clientKey: "doc-row-1", columnKey: "quantity" }
            : undefined,
    );
    form.setFields(
      mapped.form.map((item) => ({
        name: item.name[0] as keyof HeaderForm,
        errors: item.errors,
      })),
    );
    for (const cell of mapped.cells)
      errorsRef.current.set(
        `${cell.clientKey}:${cell.columnKey}`,
        cell.errors.join("；"),
      );
    pageErrorsRef.current = mapped.page;
    publish();
  }
  /** 区域先全量核权、重定位及校验，再统一写入缓冲；任一无效源不会产生部分填充。 */
  function fillDown(
    intent: SwiftDataGridFillDownIntent<EditableRow<DocumentLine>>,
  ) {
    if (!currentAccess() || phaseRef.current !== "idle") return;
    const patches: {
      row: EditableRow<DocumentLine>;
      field: keyof DocumentLine;
      raw: string;
    }[] = [];
    for (const { columnKey, source, targets } of intent.columns) {
      if (!lineFields.includes(columnKey as keyof DocumentLine)) return;
      const field = columnKey as keyof DocumentLine;
      const latestSource = rowsRef.current.find(
        (row) => row.clientKey === source.clientKey && !row.deleted,
      );
      if (!latestSource) return;
      const raw = cellText(latestSource, field);
      if (
        field === "categoryId" &&
        (!sessionRef.current.user?.permissions?.includes(
          "Products.Category.View",
        ) ||
          (raw !== "" &&
            !options.some(
              (option) => option.value === raw && !option.disabled,
            )))
      ) {
        notice("warning", "源分类或读取权限不允许填充，没有填充其他行。 ");
        return;
      }
      if (!parseDocumentLineField(field, raw).valid) {
        errorsRef.current.set(
          `${latestSource.clientKey}:${field}`,
          "源输入尚未通过校验，没有填充其他行。 ",
        );
        publish();
        return;
      }
      for (const target of targets) {
        const row = rowsRef.current.find(
          (item) => item.clientKey === target.clientKey && !item.deleted,
        );
        if (!row) return;
        patches.push({ row, field, raw });
      }
    }
    for (const { row, field, raw } of patches) {
      const key = `${row.clientKey}:${field}`;
      const buffer: LineBuffer = {
        clientKey: row.clientKey,
        field,
        raw,
        original: buffersRef.current.get(key)?.original ?? row.current[field],
      };
      if (
        hasInputChanges(buffer, (text) => parseDocumentLineField(field, text))
      )
        buffersRef.current.set(key, buffer);
      else buffersRef.current.delete(key);
      errorsRef.current.delete(key);
    }
    publish();
  }
  /** 下载只导出当前身份可访问的白名单材料，启动下载不证明文件已可靠保存。 */
  function exportRecovery() {
    const attempt = frozenRef.current,
      result = responseRef.current;
    if (
      phaseRef.current !== "unknown" ||
      !attempt ||
      !result ||
      !currentAccess()
    ) {
      notice("warning", "当前权限不允许保全；需现有合法业务/运维渠道介入。 ");
      return;
    }
    recordRef.current = null;
    let url: string | undefined;
    try {
      const id = crypto.randomUUID();
      url = URL.createObjectURL(
        new Blob(
          [
            JSON.stringify(
              createDocumentRecoveryRecord(id, attempt, result),
              null,
              2,
            ),
          ],
          { type: "application/json;charset=utf-8" },
        ),
      );
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `SwiftExport-恢复记录-${id}.json`;
      document.body.append(anchor);
      try {
        anchor.click();
      } finally {
        anchor.remove();
      }
      recordRef.current = id;
      notice(
        "info",
        `已发起下载，记录标识：${id}；程序不能确认文件已妥善保存。`,
      );
    } catch {
      notice("error", "导出失败，未完成保全，继续保留恢复限制。 ");
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }
  /** 人工终止需下载材料和两次明确确认，批量关闭及普通放弃无法调用这个出口。 */
  async function terminateRecovery() {
    const id = recordRef.current;
    if (phaseRef.current !== "unknown" || !id || !currentAccess()) {
      notice("warning", "未完成合法导出，不能终止本地恢复。 ");
      return;
    }
    const kept = await modal.confirm({
      title: "确认记录已妥善保留",
      content: `请确认已保留 SwiftExport-恢复记录-${id}.json，记录标识：${id}。下载提示本身不能证明文件保存。`,
      okText: "已妥善保留",
      cancelText: "取消",
    });
    if (
      !kept ||
      !currentAccess() ||
      phaseRef.current !== "unknown" ||
      recordRef.current !== id
    )
      return;
    const ended = await modal.confirm({
      title: "确认终止本地恢复",
      content:
        "终止本地恢复会释放本地草稿，服务器结果仍未确认，不能据此认定失败、回滚或安全重发。",
      okText: "终止并关闭",
      cancelText: "取消",
      okButtonProps: { danger: true },
    });
    if (
      !ended ||
      !currentAccess() ||
      phaseRef.current !== "unknown" ||
      recordRef.current !== id
    )
      return;
    if (!host.closeAfterManualTermination(id))
      notice("warning", "宿主未接受人工终止，继续保留恢复材料。 ");
  }

  const columns: SwiftDataGridColumn<EditableRow<DocumentLine>>[] =
    lineFields.map((field) => {
      const feedback = {
        getLabel: (_row: EditableRow<DocumentLine>, index: number) =>
          `明细${index + 1}${fieldTitles[field]}`,
        getError: (row: EditableRow<DocumentLine>) =>
          errorsRef.current.get(`${row.clientKey}:${field}`),
        onCancel: (row: EditableRow<DocumentLine>) => cancelCell(row, field),
      };
      if (field === "name" || field === "quantity")
        return {
          key: field,
          title: fieldTitles[field],
          width: 200,
          editor: {
            ...feedback,
            inputMode: field === "quantity" ? "numeric" : undefined,
            getText: (row: EditableRow<DocumentLine>) => cellText(row, field),
            onChange: (row: EditableRow<DocumentLine>, text: string) =>
              changeCell(row, field, text),
          },
        };
      return {
        key: field,
        title: fieldTitles[field],
        width: 210,
        allowFillDown: true,
        readOnly: field === "categoryId" && !canReadCategory,
        editor: {
          ...feedback,
          kind: "custom",
          render: (context) => {
            const value = cellText(context.row, field);
            if (field === "kind")
              return (
                <LookupSelect
                  {...context.inputAttributes}
                  label={feedback.getLabel(context.row, context.index)}
                  value={value || null}
                  readOnly={context.readOnly}
                  options={[
                    { value: "normal", label: "普通" },
                    { value: "fragile", label: "易碎" },
                  ]}
                  mode="local"
                  searchValue={
                    searchRef.current.get(`${context.row.clientKey}:kind`) ?? ""
                  }
                  onSearchChange={(text) => {
                    searchRef.current.set(
                      `${context.row.clientKey}:kind`,
                      text,
                    );
                    publish();
                  }}
                  onChange={(next) =>
                    changeCell(context.row, field, next ?? "")
                  }
                />
              );
            if (field === "businessDate")
              return (
                <DatePicker
                  {...context.inputAttributes}
                  value={value ? dayjs(value) : null}
                  inputReadOnly
                  disabled={context.readOnly}
                  status={context.error ? "error" : undefined}
                  onChange={(date) =>
                    changeCell(
                      context.row,
                      field,
                      date?.format("YYYY-MM-DD") ?? "",
                    )
                  }
                />
              );
            const key = context.row.clientKey;
            const search = searchRef.current.get(key) ?? "";
            return (
              <LookupSelect
                {...context.inputAttributes}
                label={feedback.getLabel(context.row, context.index)}
                value={value || null}
                options={options.filter((option) =>
                  option.label
                    .toLocaleLowerCase()
                    .includes(search.trim().toLocaleLowerCase()),
                )}
                selectedOption={
                  value
                    ? { value, label: labelsRef.current.get(value) ?? value }
                    : undefined
                }
                mode="remote"
                searchValue={search}
                onSearchChange={(text) => {
                  searchRef.current.set(key, text);
                  publish();
                }}
                onChange={(next) => {
                  const selected = options.find(
                    (option) => option.value === next,
                  );
                  if (selected)
                    labelsRef.current.set(selected.value, selected.label);
                  changeCell(context.row, field, next ?? "");
                }}
                readOnly={context.readOnly}
                loading={canReadCategory && categories.isFetching}
              />
            );
          },
        },
      };
    });
  columns.push({
    key: "version",
    title: "基线版本",
    readOnly: true,
    width: 120,
    render: (row) => row.version,
  });
  const summary = getSummary();
  return renderDisplay(
    <ConnectedForm connected={connect}>
      <Space
        orientation="vertical"
        style={{ width: "100%" }}
        data-testid={`document-verification-${descriptor.tabId}`}
      >
        <Card title="头行统一编辑验收">
          <Space orientation="vertical" style={{ width: "100%" }}>
            <Typography.Text>
              保存状态：{phaseRef.current}；提交尝试：{attemptsRef.current}
              ；头版本：{headerVersionRef.current}
            </Typography.Text>
            <Typography.Text>
              业务修改：{summary.dirty ? "是" : "否"}；未提交输入：
              {summary.hasUncommittedInput ? "是" : "否"}
            </Typography.Text>
            <Typography.Text type="secondary">
              保存仅为本地 A/B/C
              验收；分类由已有读取接口提供，搜索本地过滤，不发送真实业务写入。
            </Typography.Text>
            {!canRead ? (
              <Alert
                type="warning"
                title="当前身份或权限不能读取本页，草稿仍由原会话保留。"
              />
            ) : (
              <>
                {phaseRef.current === "unknown" && (
                  <RecoveryActions
                    title="头行结果未知：保留锁与冻结材料"
                    description="不自动重提、不普通放弃；先依法保全，再由用户两次确认人工终止。"
                    onExport={exportRecovery}
                    onTerminate={() => void terminateRecovery()}
                  />
                )}
                <ProForm<HeaderForm>
                  name={`document-${descriptor.tabId}`}
                  form={form}
                  initialValues={initialForm}
                  layout="vertical"
                  preserve
                  disabled={locked || !canEdit}
                  onValuesChange={publish}
                  submitter={false}
                  omitNil={false}
                  dateFormatter={false}
                  syncToUrl={false}
                  syncToInitialValues={false}
                  autoFocusFirstInput={false}
                  isKeyPressSubmit={false}
                >
                  <Form.Item
                    name="name"
                    label="单据名称"
                    rules={[
                      {
                        required: true,
                        whitespace: true,
                        message: "单据名称不能为空。",
                      },
                    ]}
                  >
                    <Input />
                  </Form.Item>
                  <Form.Item
                    name="amount"
                    label="单据金额"
                    rules={[
                      {
                        validator: async (_, value: string) => {
                          if (!parseDecimalText(value ?? "", true).valid)
                            throw new Error("金额必须是完整十进制数或空值。 ");
                        },
                      },
                    ]}
                  >
                    <Input inputMode="decimal" />
                  </Form.Item>
                  <Form.Item name="businessDate" label="业务日期">
                    <DatePicker inputReadOnly />
                  </Form.Item>
                </ProForm>
                {!canReadCategory && (
                  <Alert
                    type="info"
                    title="无分类读取权限：分类列只读，不扩大账号权限。"
                  />
                )}
                {canReadCategory && (
                  <Button
                    disabled={categories.isFetching || !isActive}
                    onClick={() => {
                      if (
                        currentAccess(false) &&
                        sessionRef.current.user?.permissions?.includes(
                          "Products.Category.View",
                        )
                      )
                        void categories.refetch();
                    }}
                  >
                    {categories.isError ? "重新读取分类" : "刷新分类候选"}
                  </Button>
                )}
                {canReadCategory && categories.isError && (
                  <Alert
                    type="error"
                    title={describeApiError(categories.error)}
                  />
                )}
                <PreferenceDataGrid
                  preferenceId="verification.document.lines"
                  rows={rowsRef.current}
                  getRowKey={(row) => row.clientKey}
                  columns={columns}
                  readOnly={locked || !canEdit}
                  selectedKeys={selectionRef.current}
                  onSelectionChange={(keys) => {
                    selectionRef.current = keys;
                    publish();
                  }}
                  onFillDown={fillDown}
                  showRowNumber
                />
                {pageErrorsRef.current.map((text) => (
                  <Alert key={text} type="error" title={text} />
                ))}
                <Space wrap>
                  <Select
                    aria-label="头行保存场景"
                    value={scenarioRef.current}
                    options={scenarioOptions}
                    disabled={locked}
                    style={{ width: 190 }}
                    onChange={(value: DocumentScenario) => {
                      scenarioRef.current = value;
                      publish();
                    }}
                  />
                  <Button
                    type="primary"
                    disabled={locked || !canEdit}
                    onClick={() => void requestSave()}
                  >
                    保存头行
                  </Button>
                  <Button
                    disabled={locked}
                    onClick={async () => {
                      const accepted = await modal.confirm({
                        title: "放弃头行修改？",
                        content: "恢复头和全部明细基线，清除未提交输入。",
                        okText: "放弃",
                        cancelText: "取消",
                      });
                      if (accepted) await controlsRef.current!.requestDiscard();
                    }}
                  >
                    放弃头行修改
                  </Button>
                  <Button disabled={locked} onClick={simulateFieldErrors}>
                    模拟字段错误
                  </Button>
                </Space>
              </>
            )}
            {noticeRef.current && (
              <Alert
                type={noticeRef.current.type}
                title={noticeRef.current.text}
                role="status"
              />
            )}
          </Space>
        </Card>
      </Space>
    </ConnectedForm>,
  );
}
