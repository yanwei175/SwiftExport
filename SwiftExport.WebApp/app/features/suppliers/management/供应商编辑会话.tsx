import { useEffect, useRef, useState } from "react";
import { App, Button, Space } from "antd";
import {
  useQuery,
  useQueryClient,
  isCancelledError,
} from "@tanstack/react-query";
import type { SwiftExportContractsDTOsSuppliersRequestsSupplierSearchRequest as SupplierSearchRequest } from "../../../shared/api/generated/client";
import type { PermissionKey } from "../../../shared/authorization/generated/权限键";
import { hasPermission } from "../../../shared/authorization/权限检查";
import { RequestTransportError } from "../../../shared/api/请求执行";
import { describeApiError } from "../../../shared/api/响应错误";
import { useSession } from "../../../shared/session/会话上下文";
import { queryKeys } from "../../../shared/query/查询键";
import { queryPolicies } from "../../../shared/query/查询策略";
import { useUserPreferences } from "../../../shared/preferences/用户偏好上下文";
import { resolveGridPreferences } from "../../../shared/preferences/用户偏好";
import type { PreferencePageSize } from "../../../shared/preferences/用户偏好";
import {
  getEditableChanges,
  isRowDirty,
  updateEditableRow,
  removeEditableRow,
  restoreEditableRow,
  discardEditableRows,
} from "../../../shared/editing/编辑集合";
import { hasInputChanges } from "../../../shared/editing/未提交输入";
import type { UncommittedInput } from "../../../shared/editing/未提交输入";
import { useWorkspaceTab } from "../../../shared/workspace/useWorkspaceTab";
import { isUnprotected } from "../../../shared/workspace/页签契约";
import type {
  WorkspaceTabProps,
  ProtectionSummary,
  SaveDecision,
  DiscardDecision,
  TabControl,
} from "../../../shared/workspace/页签契约";
import type {
  SwiftDataGridColumn,
  SwiftDataGridRowKey,
  SwiftDataGridFillDownIntent,
} from "../../../shared/ui/swift-data-grid";
import {
  createSupplierRows,
  createSupplierRow,
  parseSupplierInput,
  validateSupplierValues,
  supplierFieldTitles,
} from "./model/供应商模型";
import type {
  SupplierRow,
  SupplierField,
  SupplierValues,
} from "./model/供应商模型";
import {
  freezeSupplierRows,
  createSupplierRecoveryRecord,
} from "./model/提交契约";
import type { FrozenSuppliers, SupplierSaveEvidence } from "./model/提交契约";
import { fetchSupplierPage, invalidateSupplierQueries } from "./queries";
import { saveSuppliers } from "./mutations";

type Phase = "idle" | "loading" | "preparing" | "saving" | "unknown";
type CellBuffer = UncommittedInput<
  SupplierField,
  SupplierValues[SupplierField]
>;
type Notice = {
  type: "success" | "warning" | "error" | "info";
  text: string;
  persistent?: boolean;
};
const permission = {
  view: "Suppliers.Supplier.View",
  create: "Suppliers.Supplier.Create",
  edit: "Suppliers.Supplier.Edit",
  delete: "Suppliers.Supplier.Delete",
} as const satisfies Record<string, PermissionKey>;
const fields = Object.keys(supplierFieldTitles) as SupplierField[];
const definitions = fields.map((id) => ({
  id,
  defaultWidth: id === "address" || id === "zheKouBeiZhu" ? 260 : 170,
}));

/** 读取失败只报告快照未取得，不能沿用写入结果未知的提示。 */
function readError(error: unknown): string {
  if (
    isCancelledError(error) ||
    (error instanceof RequestTransportError && error.kind === "cancelled")
  )
    return "供应商读取已取消，原编辑内容保留。";
  if (error instanceof RequestTransportError)
    return "供应商读取失败，请检查网络或会话后重试，原编辑内容保留。";
  return describeApiError(error);
}

/** 每个供应商列表页签拥有独立编辑集合和原始缓冲，Activity 仅管理展示树。 */
export function useSupplierEditor({ descriptor, isActive }: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const queryClient = useQueryClient();
  const preferences = useUserPreferences();
  const { modal } = App.useApp();
  const [, revise] = useState(0);
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const activeRef = useRef(isActive);
  activeRef.current = isActive;
  const ownerRef = useRef({
    userId: session.user?.userId,
    epoch: session.identityEpoch,
  });
  const aliveRef = useRef(true);
  const initializedRef = useRef(false);
  const rowsRef = useRef<SupplierRow[]>([]);
  const totalRef = useRef(0);
  const requestRef = useRef<SupplierSearchRequest>({
    keyword: "",
    batchKeywords: [],
    pageIndex: 1,
    pageSize: 20,
  });
  const buffersRef = useRef(new Map<string, CellBuffer>());
  const errorsRef = useRef(new Map<string, string>());
  const selectedRef = useRef<readonly SwiftDataGridRowKey[]>([]);
  const phaseRef = useRef<Phase>("idle");
  const noticeRef = useRef<Notice | null>(null);
  const readingRef = useRef<AbortController | null>(null);
  const frozenRef = useRef<FrozenSuppliers | null>(null);
  const evidenceRef = useRef<SupplierSaveEvidence | null>(null);
  const recordRef = useRef<string | null>(null);
  const controlRef = useRef<TabControl | null>(null);
  const initializeRef = useRef<() => void>(() => undefined);
  const preferencesRef = useRef(preferences);
  preferencesRef.current = preferences;
  const canRead =
    session.status === "authenticated" &&
    hasPermission(session.user, permission.view);
  const pageSize = !initializedRef.current
    ? (preferences.grids["supplier-management"]?.pageSize ?? 20)
    : requestRef.current.pageSize;
  const currentRequest = { ...requestRef.current, pageSize };
  const identity = {
    userId: session.user?.userId ?? "",
    identityEpoch: session.identityEpoch,
    permissionEpoch: session.permissionEpoch,
  };
  const initialQuery = useQuery({
    ...queryPolicies.live,
    queryKey: queryKeys.operation(identity, "getSupplierPage", currentRequest),
    queryFn: ({ signal }) => fetchSupplierPage(currentRequest, signal),
    enabled:
      isActive &&
      canRead &&
      preferences.storageStatus !== "not-loaded" &&
      phaseRef.current !== "loading",
  });

  /** 权限每次按当前身份核对；菜单可见或旧权限快照不能授权本次写入。 */
  function access(operation: keyof typeof permission = "view"): boolean {
    const now = sessionRef.current;
    return (
      now.status === "authenticated" &&
      now.user?.userId === ownerRef.current.userId &&
      now.identityEpoch === ownerRef.current.epoch &&
      hasPermission(now.user, permission.view) &&
      hasPermission(now.user, permission[operation])
    );
  }
  function queryIdentity() {
    const now = sessionRef.current;
    return {
      userId: now.user?.userId ?? "",
      identityEpoch: now.identityEpoch,
      permissionEpoch: now.permissionEpoch,
    };
  }
  /** 同一最新摘要用于保存、普通放弃、关闭和数据替换，未提交输入与业务修改分别计算。 */
  function summary(): ProtectionSummary {
    return {
      dirty: rowsRef.current.some(isRowDirty),
      hasUncommittedInput: [...buffersRef.current.values()].some((buffer) =>
        hasInputChanges(buffer, (raw) => parseSupplierInput(buffer.field, raw)),
      ),
      preparing:
        phaseRef.current === "preparing" || phaseRef.current === "loading",
      saving: phaseRef.current === "saving",
      unknown: phaseRef.current === "unknown",
    };
  }
  function publish(): void {
    if (!aliveRef.current) return;
    host.reportSummary(summary());
    revise((value) => value + 1);
  }
  function notice(
    type: Notice["type"],
    text: string,
    persistent = false,
  ): void {
    noticeRef.current = { type, text, persistent };
    publish();
  }
  /** 仅显式首次读取或经过完整保护的重载接纳快照，后台 Query 更新不覆盖草稿。 */
  function replace(
    data: Awaited<ReturnType<typeof fetchSupplierPage>>,
    request: SupplierSearchRequest,
  ): void {
    rowsRef.current = createSupplierRows(data.items);
    totalRef.current = data.totalCount;
    requestRef.current = { ...request };
    initializedRef.current = true;
    buffersRef.current.clear();
    errorsRef.current.clear();
    selectedRef.current = [];
    noticeRef.current = null;
  }
  function rowCanEdit(row: SupplierRow): boolean {
    return (
      phaseRef.current === "idle" &&
      !row.deleted &&
      access(row.origin === "created" ? "create" : "edit")
    );
  }
  function writeAllowed(
    rows: readonly SupplierRow[] = rowsRef.current,
  ): boolean {
    const changes = getEditableChanges(rows);
    return (
      access() &&
      (!changes.created.length || access("create")) &&
      (!changes.updated.length || access("edit")) &&
      (!changes.deleted.length || access("delete"))
    );
  }
  function cellText(row: SupplierRow, field: SupplierField): string {
    return (
      buffersRef.current.get(`${row.clientKey}:${field}`)?.raw ??
      String(row.current[field] ?? "")
    );
  }
  /** 输入原文属于短期缓冲；非法或未完成数字保留，恢复原值只解除对应单元格保护。 */
  function changeCell(
    row: SupplierRow,
    field: SupplierField,
    raw: string,
  ): void {
    const latest = rowsRef.current.find(
      (item) => item.clientKey === row.clientKey,
    );
    if (!latest || !rowCanEdit(latest)) return;
    const key = `${row.clientKey}:${field}`;
    const buffer = {
      clientKey: row.clientKey,
      field,
      raw,
      original: buffersRef.current.get(key)?.original ?? latest.current[field],
    };
    // 同值格式仍可能处于输入过程（如 0 → 0.0 → 0.05）；只清除与原文规范值完全一致的缓冲。
    // 保护摘要继续按转换后的业务值比较，同值格式不会被当作未保存修改。
    if (
      !hasInputChanges(buffer, (value) => parseSupplierInput(field, value)) &&
      raw === String(buffer.original ?? "")
    )
      buffersRef.current.delete(key);
    else buffersRef.current.set(key, buffer);
    errorsRef.current.delete(key);
    publish();
  }
  function cancelCell(row: SupplierRow, field: SupplierField): void {
    if (!rowCanEdit(row)) return;
    buffersRef.current.delete(`${row.clientKey}:${field}`);
    errorsRef.current.delete(`${row.clientKey}:${field}`);
    publish();
  }
  /** 先转换全部原文，再接纳集合并验证本次变更；任一非法输入不会发送请求。 */
  function commitInputs(): boolean {
    errorsRef.current.clear();
    for (const [key, buffer] of buffersRef.current) {
      if (
        rowsRef.current.find((row) => row.clientKey === buffer.clientKey)
          ?.deleted
      )
        continue;
      if (!parseSupplierInput(buffer.field, buffer.raw).valid)
        errorsRef.current.set(
          key,
          `${supplierFieldTitles[buffer.field]}无效或尚未输入完成。`,
        );
    }
    if (errorsRef.current.size) return false;
    for (const [key, buffer] of buffersRef.current) {
      if (
        rowsRef.current.find((row) => row.clientKey === buffer.clientKey)
          ?.deleted
      )
        continue;
      const parsed = parseSupplierInput(buffer.field, buffer.raw);
      if (parsed.valid)
        rowsRef.current = updateEditableRow(rowsRef.current, buffer.clientKey, {
          [buffer.field]: parsed.value,
        }) as SupplierRow[];
      buffersRef.current.delete(key);
    }
    const changes = getEditableChanges(rowsRef.current);
    for (const row of [...changes.created, ...changes.updated]) {
      for (const [field, text] of Object.entries(
        validateSupplierValues(row.current),
      ))
        if (text) errorsRef.current.set(`${row.clientKey}:${field}`, text);
    }
    return errorsRef.current.size === 0;
  }
  /** 字段定位只面向本页展示树；列被隐藏时保留页级错误，不借索引定位另一标签。 */
  function focusFirstError(): void {
    const first = errorsRef.current.keys().next().value as string | undefined;
    if (!first) return;
    const rowIndex = rowsRef.current
      .filter((row) => !row.deleted)
      .findIndex((row) => first.startsWith(`${row.clientKey}:`));
    const field = first.slice(first.lastIndexOf(":") + 1) as SupplierField;
    const container = document.querySelector(
      `[data-supplier-tab="${descriptor.tabId}"]`,
    );
    const input = container?.querySelector<HTMLElement>(
      `[aria-label="供应商${rowIndex + 1}${supplierFieldTitles[field]}"]`,
    );
    input?.focus();
    input?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }
  /** 工具栏及关闭保存复用此入口；准备、派发、完整回执接纳期间均冻结业务编辑。 */
  async function requestSave(): Promise<SaveDecision> {
    if (phaseRef.current !== "idle")
      return phaseRef.current === "unknown" ? "unknown" : "failed";
    if (!access()) return "failed";
    if (isUnprotected(summary())) return "saved";
    phaseRef.current = "preparing";
    recordRef.current = null;
    publish();
    try {
      await host.ensureVisible();
      if (!aliveRef.current || !access())
        throw new Error("当前身份或展示条件改变，未发送请求。");
      if (!commitInputs()) {
        focusFirstError();
        throw new Error("供应商输入校验失败，原始输入与草稿保留，未发送请求。");
      }
      if (!writeAllowed())
        throw new Error("本次供应商增改删权限不足，未发送请求。");
      if (!rowsRef.current.some(isRowDirty)) {
        phaseRef.current = "idle";
        publish();
        return isUnprotected(summary()) ? "saved" : "unsaved";
      }
      const frozen = freezeSupplierRows(rowsRef.current);
      frozenRef.current = frozen;
      evidenceRef.current = null;
      phaseRef.current = "saving";
      publish();
      const result = await saveSuppliers(frozen);
      if (
        !aliveRef.current ||
        sessionRef.current.user?.userId !== ownerRef.current.userId ||
        sessionRef.current.identityEpoch !== ownerRef.current.epoch
      )
        return "unknown";
      evidenceRef.current = result;
      if (result.kind === "unknown") {
        phaseRef.current = "unknown";
        notice("error", result.message);
        return "unknown";
      }
      if (result.kind === "confirmedNoEffect") {
        phaseRef.current = "idle";
        notice("warning", result.message);
        return "failed";
      }
      rowsRef.current = result.acceptedRows;
      buffersRef.current.clear();
      errorsRef.current.clear();
      totalRef.current = Math.max(
        0,
        totalRef.current +
          (frozen.request.createItems?.length ?? 0) -
          (frozen.request.deleteItems?.length ?? 0),
      );
      selectedRef.current = [];
      phaseRef.current = "idle";
      void invalidateSupplierQueries(queryClient, queryIdentity()).catch(
        () => undefined,
      );
      notice("success", "供应商保存完成。");
      return isUnprotected(summary()) ? "saved" : "unsaved";
    } catch (error) {
      if (!aliveRef.current) return "unknown";
      if (phaseRef.current === "saving") {
        phaseRef.current = "unknown";
        notice("error", "派发后无法核对完整结果，保留冻结材料与恢复限制。");
        return "unknown";
      }
      phaseRef.current = "idle";
      notice("error", describeApiError(error));
      return "failed";
    }
  }
  /** 放弃只在无保存锁且当前仍可读时恢复基线；未知状态不能借放弃变为可重提。 */
  async function requestDiscard(): Promise<DiscardDecision> {
    if (phaseRef.current !== "idle" || !access()) return "blocked";
    rowsRef.current = discardEditableRows(rowsRef.current) as SupplierRow[];
    buffersRef.current.clear();
    errorsRef.current.clear();
    selectedRef.current = [];
    noticeRef.current = null;
    publish();
    return "discarded";
  }
  /** 保存/放弃/取消决定是否允许替换；放弃选择不提前清草稿，重载失败保持原会话。 */
  async function authorizeReplacement(): Promise<boolean> {
    if (phaseRef.current !== "idle" || !access()) return false;
    if (isUnprotected(summary())) return true;
    const choice = await new Promise<"save" | "discard" | "cancel">(
      (resolve) => {
        const dialog = modal.confirm({
          title: "处理未保存供应商修改",
          content: "当前供应商修改或未提交输入需要保护，请选择处理方式。",
          onCancel: () => resolve("cancel"),
          footer: () => (
            <Space>
              <Button
                onClick={() => {
                  dialog.destroy();
                  resolve("cancel");
                }}
              >
                取消
              </Button>
              <Button
                danger
                onClick={() => {
                  dialog.destroy();
                  resolve("discard");
                }}
              >
                放弃并继续
              </Button>
              <Button
                type="primary"
                onClick={() => {
                  dialog.destroy();
                  resolve("save");
                }}
              >
                保存并继续
              </Button>
            </Space>
          ),
        });
      },
    );
    if (choice === "cancel" || phaseRef.current !== "idle" || !access())
      return false;
    return (
      choice === "discard" ||
      ((await requestSave()) === "saved" &&
        isUnprotected(summary()) &&
        access())
    );
  }
  /** 筛选、分页及刷新先处理保护，再完整读回；晚到/失权/隐藏的响应不替换原会话。 */
  async function load(request: SupplierSearchRequest): Promise<void> {
    if (!(await authorizeReplacement()) || !activeRef.current) return;
    const scope = queryIdentity();
    const controller = new AbortController();
    readingRef.current = controller;
    phaseRef.current = "loading";
    publish();
    try {
      const data = await queryClient.fetchQuery({
        ...queryPolicies.live,
        queryKey: queryKeys.operation(scope, "getSupplierPage", request),
        queryFn: () => fetchSupplierPage(request, controller.signal),
        staleTime: 0,
      });
      if (
        controller.signal.aborted ||
        !aliveRef.current ||
        !activeRef.current ||
        !access() ||
        sessionRef.current.permissionEpoch !== scope.permissionEpoch
      )
        return;
      replace(data, request);
      if (
        request.pageSize === 20 ||
        request.pageSize === 50 ||
        request.pageSize === 100
      ) {
        const currentPreferences = preferencesRef.current;
        const layout = resolveGridPreferences(
          currentPreferences.grids["supplier-management"],
          definitions,
        );
        currentPreferences.setGridPreferences(
          "supplier-management",
          { ...layout, pageSize: request.pageSize as PreferencePageSize },
          definitions,
        );
      }
    } catch (error) {
      if (!controller.signal.aborted && access() && activeRef.current)
        noticeRef.current = {
          type: "error",
          text: readError(error),
          persistent: true,
        };
    } finally {
      if (readingRef.current === controller) {
        readingRef.current = null;
        phaseRef.current = "idle";
        publish();
      }
    }
  }
  async function discard(): Promise<void> {
    if (phaseRef.current !== "idle" || !access()) return;
    const confirmed = await modal.confirm({
      title: "放弃供应商修改？",
      content: "恢复当前页基线并清除新增行及未提交输入。",
      okText: "放弃修改",
      cancelText: "取消",
    });
    if (confirmed) await requestDiscard();
  }
  function add(): void {
    if (
      !initializedRef.current ||
      phaseRef.current !== "idle" ||
      !access("create")
    )
      return;
    rowsRef.current = [...rowsRef.current, createSupplierRow()];
    publish();
  }
  function deleteSelected(): void {
    if (phaseRef.current !== "idle" || !access()) return;
    for (const row of rowsRef.current) {
      if (
        !selectedRef.current.includes(row.clientKey) ||
        (row.origin !== "created" && !access("delete"))
      )
        continue;
      rowsRef.current = removeEditableRow(
        rowsRef.current,
        row.clientKey,
      ) as SupplierRow[];
      // 既有删除行保留原文供恢复；新增行真正移除后才清理其缓冲。
      if (row.origin === "created")
        for (const key of buffersRef.current.keys())
          if (key.startsWith(`${row.clientKey}:`))
            buffersRef.current.delete(key);
      for (const key of errorsRef.current.keys())
        if (key.startsWith(`${row.clientKey}:`)) errorsRef.current.delete(key);
    }
    selectedRef.current = [];
    publish();
  }
  function restoreDeleted(): void {
    if (phaseRef.current !== "idle" || !access()) return;
    for (const row of rowsRef.current.filter((item) => item.deleted))
      rowsRef.current = restoreEditableRow(
        rowsRef.current,
        row.clientKey,
      ) as SupplierRow[];
    publish();
  }
  /** 表格只发区域意图；feature 重新核对字段白名单、所有源和目标，整次合法才创建缓冲。 */
  function fillDown(intent: SwiftDataGridFillDownIntent<SupplierRow>): void {
    const proposed = new Map<
      string,
      { row: SupplierRow; field: SupplierField; raw: string }
    >();
    for (const column of intent.columns) {
      const field = column.columnKey as SupplierField;
      const source = rowsRef.current.find(
        (row) => row.clientKey === column.source.clientKey,
      );
      if (
        !fields.includes(field) ||
        column.fields.length !== 1 ||
        column.fields[0] !== field ||
        !source ||
        !rowCanEdit(source) ||
        !parseSupplierInput(field, cellText(source, field)).valid
      ) {
        notice("warning", "填充源输入、字段或权限无效，未应用填充。");
        return;
      }
      const raw = cellText(source, field);
      for (const target of column.targets) {
        const row = rowsRef.current.find(
          (item) => item.clientKey === target.clientKey,
        );
        const key = `${target.clientKey}:${field}`;
        if (
          !row ||
          !rowCanEdit(row) ||
          (proposed.has(key) && proposed.get(key)!.raw !== raw)
        ) {
          notice("warning", "填充目标或字段冲突，未应用填充。");
          return;
        }
        proposed.set(key, { row, field, raw });
      }
    }
    for (const item of proposed.values())
      changeCell(item.row, item.field, item.raw);
  }
  /** 恢复材料要求当前同一身份及冻结批次全部操作权，不能借导出绕过撤权。 */
  function recoveryAllowed(): boolean {
    return !!frozenRef.current && writeAllowed(frozenRef.current.rows);
  }
  /** 恢复导出仅保全白名单核实材料，触发下载不代表文件已可靠保存。 */
  function exportRecovery(): void {
    recordRef.current = null;
    if (
      phaseRef.current !== "unknown" ||
      !frozenRef.current ||
      !recoveryAllowed()
    ) {
      notice("warning", "当前权限不允许保全，需现有合法业务/运维渠道介入。");
      return;
    }
    let url: string | undefined;
    try {
      const recordId = crypto.randomUUID();
      const record = createSupplierRecoveryRecord(
        recordId,
        frozenRef.current,
        evidenceRef.current ?? { message: "服务器结果仍未确认。" },
      );
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
      recordRef.current = recordId;
      notice(
        "info",
        `已发起下载，记录标识：${recordId}；程序不能确认文件已妥善保存。`,
      );
    } catch {
      notice("error", "导出失败，继续保留恢复限制。");
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }
  /** 合法导出及两次明确确认后仅关闭原页签，不将未知结果转换为失败或成功。 */
  async function terminateRecovery(): Promise<void> {
    const record = recordRef.current;
    if (phaseRef.current !== "unknown" || !record || !recoveryAllowed()) {
      notice("warning", "未完成合法导出，不能终止本地恢复。");
      return;
    }
    const kept = await modal.confirm({
      title: "确认记录已妥善保留",
      content: `请确认已保留 SwiftExport-恢复记录-${record}.json，记录标识：${record}。下载提示不能证明文件保存。`,
      okText: "已妥善保留",
      cancelText: "取消",
    });
    if (
      !kept ||
      recordRef.current !== record ||
      phaseRef.current !== "unknown" ||
      !recoveryAllowed()
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
      ended &&
      recordRef.current === record &&
      phaseRef.current === "unknown" &&
      recoveryAllowed() &&
      !host.closeAfterManualTermination(record)
    )
      notice("warning", "宿主未接受人工终止，恢复限制仍保留。");
  }
  /** 独立读回仅辅助核实，不接纳草稿，不凭值相同认定整批成功或安全重发。 */
  async function inspectRecovery(): Promise<void> {
    if (phaseRef.current !== "unknown" || !recoveryAllowed()) return;
    const scope = queryIdentity();
    try {
      const data = await queryClient.fetchQuery({
        ...queryPolicies.live,
        queryKey: queryKeys.operation(
          scope,
          "getSupplierPage",
          requestRef.current,
        ),
        queryFn: ({ signal }) => fetchSupplierPage(requestRef.current, signal),
        staleTime: 0,
      });
      if (
        !recoveryAllowed() ||
        sessionRef.current.permissionEpoch !== scope.permissionEpoch
      )
        return;
      notice(
        "info",
        `已独立读取当前查询的 ${data.items.length} 条供应商。读取不证明整批提交结果；草稿与冻结材料保留，请由有权限人员按合法渠道核实。`,
      );
    } catch (error) {
      if (access()) notice("error", readError(error));
    }
  }

  controlRef.current = {
    getSummary: summary,
    canClose: () =>
      isUnprotected(summary())
        ? { allowed: true }
        : {
            allowed: false,
            reason: "供应商修改、未提交输入或保存恢复尚未处理。",
          },
    requestSave,
    requestDiscard,
  };
  initializeRef.current = () => {
    if (
      !initializedRef.current &&
      initialQuery.data &&
      isActive &&
      canRead &&
      phaseRef.current === "idle"
    ) {
      replace(initialQuery.data, currentRequest);
      publish();
    }
  };
  useEffect(
    () =>
      host.registerControl({
        getSummary: () => controlRef.current!.getSummary(),
        canClose: () => controlRef.current!.canClose(),
        requestSave: () => controlRef.current!.requestSave(),
        requestDiscard: () => controlRef.current!.requestDiscard(),
      }),
    [host],
  );
  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
      readingRef.current?.abort();
    };
  }, []);
  useEffect(() => {
    initializeRef.current();
  }, [initialQuery.data, isActive, canRead, pageSize]);
  useEffect(() => {
    if (!isActive || !canRead) {
      readingRef.current?.abort();
      void queryClient.cancelQueries({
        queryKey: queryKeys.operation(
          queryIdentity(),
          "getSupplierPage",
          requestRef.current,
        ),
        exact: true,
      });
    }
    host.reportSummary(controlRef.current!.getSummary());
  }, [isActive, canRead, session.permissionEpoch, host, queryClient]);

  const locked = phaseRef.current !== "idle";
  const columns: SwiftDataGridColumn<SupplierRow>[] = fields.map((field) => ({
    key: field,
    title: supplierFieldTitles[field],
    width: definitions.find((column) => column.id === field)!.defaultWidth,
    editor: {
      getLabel: (_row, index) =>
        `供应商${index + 1}${supplierFieldTitles[field]}`,
      getError: (row) => errorsRef.current.get(`${row.clientKey}:${field}`),
      getText: (row) => cellText(row, field),
      onChange: (row, raw) => changeCell(row, field, raw),
      onCancel: (row) => cancelCell(row, field),
      inputMode:
        field === "defaultLeadTime"
          ? "numeric"
          : field === "zheKou" || field === "taxRate"
            ? "decimal"
            : undefined,
    },
  }));
  return {
    tabId: descriptor.tabId,
    phase: phaseRef.current,
    summary: summary(),
    notice: noticeRef.current,
    error:
      !initializedRef.current && initialQuery.error
        ? readError(initialQuery.error)
        : null,
    columns,
    rows: rowsRef.current.filter((row) => !row.deleted),
    rowCanEdit,
    loading:
      (!initializedRef.current && initialQuery.isPending) ||
      phaseRef.current === "loading",
    readOnly: locked || !access(),
    selectedKeys: selectedRef.current,
    canCreate: initializedRef.current && !locked && access("create"),
    canSave:
      !locked &&
      (summary().dirty || summary().hasUncommittedInput) &&
      writeAllowed(),
    canDelete:
      !locked &&
      access() &&
      rowsRef.current.some(
        (row) =>
          selectedRef.current.includes(row.clientKey) &&
          (row.origin === "created" || access("delete")),
      ),
    canRestore:
      !locked && access() && rowsRef.current.some((row) => row.deleted),
    canDiscard:
      !locked && access() && (summary().dirty || summary().hasUncommittedInput),
    pagination: {
      pageIndex: requestRef.current.pageIndex ?? 1,
      pageSize: requestRef.current.pageSize ?? 20,
      total: totalRef.current,
      onChange: (pageIndex: number, pageSize: number) =>
        void load({ ...requestRef.current, pageIndex, pageSize }),
    },
    query: (keyword: string) =>
      load({ ...requestRef.current, keyword: keyword.trim(), pageIndex: 1 }),
    refresh: () => load({ ...requestRef.current, pageSize }),
    add,
    save: requestSave,
    deleteSelected,
    restoreDeleted,
    discard,
    fillDown,
    exportRecovery,
    terminateRecovery,
    inspectRecovery,
    onSelectionChange: (keys: SwiftDataGridRowKey[]) => {
      selectedRef.current = keys;
      publish();
    },
  };
}
