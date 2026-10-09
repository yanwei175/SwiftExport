import { useEffect, useRef, useState } from "react";
import { Alert, App, Button, Checkbox, Form, Space } from "antd";
import { isCancelledError, useQueryClient } from "@tanstack/react-query";
import { RequestTransportError } from "../../../shared/api/请求执行";
import { describeApiError } from "../../../shared/api/响应错误";
import { hasPermission } from "../../../shared/authorization/权限检查";
import {
  getEditableChanges,
  isRowDirty,
} from "../../../shared/editing/编辑集合";
import { hasInputChanges } from "../../../shared/editing/未提交输入";
import type {
  ParsedInput,
  UncommittedInput,
} from "../../../shared/editing/未提交输入";
import { useSession } from "../../../shared/session/会话上下文";
import { queryKeys } from "../../../shared/query/查询键";
import { queryPolicies } from "../../../shared/query/查询策略";
import { useWorkspaceTab } from "../../../shared/workspace/useWorkspaceTab";
import { isUnprotected } from "../../../shared/workspace/页签契约";
import type {
  DiscardDecision,
  ProtectionSummary,
  SaveDecision,
  TabControl,
  WorkspaceTabProps,
} from "../../../shared/workspace/页签契约";
import type {
  SwiftDataGridColumn,
  SwiftDataGridFillDownIntent,
  SwiftDataGridRowKey,
} from "../../../shared/ui/swift-data-grid";
import { dictionaryPermissions } from "./permissions";
import {
  createDictionaryItemRow,
  createDictionaryItemRows,
  discardDictionaryItemRows,
  isProtectedDictionaryType,
  parseDictionarySortOrder,
  removeDictionaryItemRows,
  restoreDictionaryItemRows,
  updateDictionaryItemRows,
  validateDictionaryItemValues,
  validateDictionaryType,
} from "./model/字典模型";
import type {
  DictionaryItemRow,
  DictionaryItemValues,
  DictionaryType,
} from "./model/字典模型";
import {
  createDictionaryRecoveryRecord,
  freezeDictionaryItems,
  freezeDictionaryType,
} from "./model/提交契约";
import type {
  DictionarySaveEvidence,
  FrozenDictionaryItems,
  FrozenDictionaryType,
} from "./model/提交契约";
import { saveDictionaryItems, saveDictionaryType } from "./mutations";
import {
  fetchDictionaryItems,
  fetchDictionaryTypes,
  invalidateDictionaryQueries,
  useDictionaryTypesQuery,
} from "./queries";

/** 类型字段当前值仅由本页 Ant Form 持有，排序原文不得被 number 控件静默丢弃。 */
export interface DictionaryTypeForm {
  dictCode: string;
  dictName: string;
  sortOrder: string;
  remark: string;
}
type ItemField =
  "dataNameEn" | "dataNameZh" | "sortOrder" | "isEnabled" | "remark";
type ItemBuffer = UncommittedInput<ItemField, DictionaryItemValues[ItemField]>;
type Phase = "idle" | "loading" | "preparing" | "saving" | "unknown";
type Notice = {
  type: "info" | "warning" | "error" | "success";
  text: string;
  /** 真实读取失败保留页内重试入口，普通操作反馈由展示层使用轻提示。 */
  persistent?: boolean;
};
type RecoveryAttempt =
  | { kind: "type"; frozen: FrozenDictionaryType }
  | { kind: "items"; frozen: FrozenDictionaryItems };
const fieldTitles: Record<ItemField, string> = {
  dataNameEn: "英文名称",
  dataNameZh: "中文名称",
  sortOrder: "排序",
  isEnabled: "启用",
  remark: "备注",
};
const itemFields = Object.keys(fieldTitles) as ItemField[];

/** 将合法业务值投影为 Form 文本，空串与可空字段沿现有 DTO 保持一致。 */
function typeFormValues(type: DictionaryType): DictionaryTypeForm {
  return {
    dictCode: type.dictCode ?? "",
    dictName: type.dictName ?? "",
    sortOrder: String(type.sortOrder ?? 0),
    remark: type.remark ?? "",
  };
}
/** 只转换当前列明确声明的原文，不把无效整数或未完成输入转换为零。 */
function parseItem(
  field: ItemField,
  raw: string,
): ParsedInput<DictionaryItemValues[ItemField]> {
  if (field === "sortOrder") return parseDictionarySortOrder(raw, true);
  if (field === "isEnabled")
    return raw === "true" || raw === "false"
      ? { valid: true, value: raw === "true" }
      : { valid: false };
  if (field === "remark" && raw === "") return { valid: true, value: null };
  return { valid: true, value: raw };
}
/** 只读 GET 的错误只说明读取失败，不沿用写入结果未知或服务器是否提交的提示。 */
function describeDictionaryReadError(error: unknown): string {
  if (
    isCancelledError(error) ||
    (error instanceof RequestTransportError && error.kind === "cancelled") ||
    (error instanceof Error && error.name === "AbortError")
  )
    return "字典读取已取消，原编辑内容保留，请重试读取。";
  if (error instanceof RequestTransportError) {
    if (error.kind === "network") return "字典读取失败，请检查网络并重试读取。";
    if (error.kind === "invalid-response")
      return "字典读取响应无法解析，原编辑内容保留，请重试读取。";
    return "字典读取尚未发送，请检查当前会话后重试读取。";
  }
  return describeApiError(error);
}
/** 字典类型与字典项共享本页保护及保存锁，两个后端端点仍各自冻结、核对和接纳。 */
export function useDictionaryEditor({
  descriptor,
  isActive,
}: WorkspaceTabProps) {
  const host = useWorkspaceTab();
  const session = useSession();
  const queryClient = useQueryClient();
  const { modal } = App.useApp();
  const [form] = Form.useForm<DictionaryTypeForm>();
  const [, revise] = useState(0);
  const sessionRef = useRef(session);
  sessionRef.current = session;
  const activeRef = useRef(isActive);
  activeRef.current = isActive;
  const ownerRef = useRef({
    userId: session.user?.userId,
    identityEpoch: session.identityEpoch,
  });
  const aliveRef = useRef(true);
  const initializedRef = useRef(false);
  // 真实首次读取失败等待用户重试；隐藏取消才允许恢复活动时重新完成初始化。
  const initialAttemptedRef = useRef(false);
  const typesRef = useRef<DictionaryType[]>([]);
  const baselineRef = useRef<DictionaryType | null>(null);
  const newTypeRef = useRef(false);
  const typeClientKeyRef = useRef<string>(crypto.randomUUID());
  const inputRevisionRef = useRef(0);
  const previousCodeRef = useRef<string | null>(null);
  const rowsRef = useRef<DictionaryItemRow[]>([]);
  const dataLoadedRef = useRef(false);
  const buffersRef = useRef(new Map<string, ItemBuffer>());
  const errorsRef = useRef(new Map<string, string>());
  const selectedRef = useRef<readonly SwiftDataGridRowKey[]>([]);
  const phaseRef = useRef<Phase>("idle");
  const noticeRef = useRef<Notice | null>(null);
  const readingRef = useRef<AbortController | null>(null);
  const frozenRef = useRef<RecoveryAttempt | null>(null);
  const responseRef = useRef<DictionarySaveEvidence | null>(null);
  const recordRef = useRef<string | null>(null);
  const controlsRef = useRef<TabControl | null>(null);
  const initializeRef = useRef<() => void>(() => undefined);
  const canRead =
    session.status === "authenticated" &&
    hasPermission(session.user, dictionaryPermissions.view);
  const typesQuery = useDictionaryTypesQuery(isActive && canRead);

  /** 从 Form 唯一当前值合成提交投影，未展示的父级、分组和系统属性仍来自基线。 */
  function currentType(): { valid: boolean; value: DictionaryType | null } {
    const baseline = baselineRef.current;
    if (!baseline) return { valid: true, value: null };
    const values = form.getFieldsValue(true);
    const sort = parseDictionarySortOrder(values.sortOrder ?? "", false);
    return {
      valid: sort.valid,
      value: {
        ...baseline,
        dictCode: values.dictCode ?? "",
        dictName: values.dictName ?? "",
        remark: values.remark ?? "",
        sortOrder: sort.valid ? (sort.value ?? 0) : baseline.sortOrder,
      },
    };
  }
  function typeDirty(): boolean {
    const current = currentType();
    return (
      newTypeRef.current ||
      !current.valid ||
      (!!current.value &&
        JSON.stringify(typeFormValues(current.value)) !==
          JSON.stringify(typeFormValues(baselineRef.current!)))
    );
  }
  function itemsDirty(): boolean {
    return rowsRef.current.some(isRowDirty);
  }
  function hasBuffers(): boolean {
    return [...buffersRef.current.values()].some((buffer) =>
      hasInputChanges(buffer, (raw) => parseItem(buffer.field, raw)),
    );
  }
  /** 查询替换同样占用准备保护，任何关闭路径都不得漏掉原始输入或结果未知。 */
  function getSummary(): ProtectionSummary {
    return {
      dirty: typeDirty() || itemsDirty(),
      hasUncommittedInput: hasBuffers(),
      preparing:
        phaseRef.current === "preparing" || phaseRef.current === "loading",
      saving: phaseRef.current === "saving",
      unknown: phaseRef.current === "unknown",
    };
  }
  function publish(): void {
    if (!aliveRef.current) return;
    host.reportSummary(getSummary());
    revise((value) => value + 1);
  }
  function notice(type: Notice["type"], text: string): void {
    noticeRef.current = { type, text };
    publish();
  }
  /** 每次动作重新核对原身份及明确操作权限；同用户重认证保留草稿，撤权不允许继续写入。 */
  function access(operation?: "create" | "edit" | "delete"): boolean {
    const now = sessionRef.current;
    return (
      now.status === "authenticated" &&
      now.identityEpoch === ownerRef.current.identityEpoch &&
      now.user?.userId === ownerRef.current.userId &&
      hasPermission(now.user, dictionaryPermissions.view) &&
      (!operation || hasPermission(now.user, dictionaryPermissions[operation]))
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
  /** 类型/值输入各自拥有当前值；替换仅发生在完整读取成功且原会话仍有权限之后。 */
  function replace(
    type: DictionaryType | null,
    rows: DictionaryItemRow[],
    types?: DictionaryType[],
  ): void {
    if (types) typesRef.current = types;
    baselineRef.current = type ? structuredClone(type) : null;
    newTypeRef.current = false;
    typeClientKeyRef.current = type?.uid ?? crypto.randomUUID();
    rowsRef.current = rows;
    dataLoadedRef.current = !!type;
    buffersRef.current.clear();
    errorsRef.current.clear();
    selectedRef.current = [];
    form.resetFields();
    if (type) form.setFieldsValue(typeFormValues(type));
    noticeRef.current = null;
  }
  /** 明确三选项确认，放弃选择本身不清草稿；目标读取失败时仍保留原会话。 */
  async function authorizeReplacement(): Promise<boolean> {
    if (phaseRef.current !== "idle" || !access()) {
      notice("warning", "保存、恢复或读取尚未结束，不能替换当前编辑数据。 ");
      return false;
    }
    if (isUnprotected(getSummary())) return true;
    const choice = await new Promise<"save" | "discard" | "cancel">(
      (resolve) => {
        const dialog = modal.confirm({
          title: "处理未保存字典修改",
          content: "当前字典类型、字典项或未提交输入需要保护，请选择处理方式。",
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
    if (choice === "discard") return true;
    return (
      (await requestSave()) === "saved" &&
      isUnprotected(getSummary()) &&
      access()
    );
  }
  /** 显式替换请求使用缓存的身份键及统一生成操作；失败、隐藏或撤权不覆盖原基线与草稿。 */
  async function loadTarget(
    type: DictionaryType | null,
    options: { types?: DictionaryType[]; guard?: boolean } = {},
  ): Promise<void> {
    if (options.guard !== false && !(await authorizeReplacement())) return;
    if (!access() || phaseRef.current !== "idle") return;
    if (!initializedRef.current && !baselineRef.current && !newTypeRef.current)
      initialAttemptedRef.current = true;
    const identity = queryIdentity();
    const inputRevision = inputRevisionRef.current;
    const controller = new AbortController();
    readingRef.current?.abort();
    readingRef.current = controller;
    phaseRef.current = "loading";
    publish();
    try {
      const items = type
        ? await queryClient.fetchQuery({
            queryKey: queryKeys.operation(
              identity,
              "getDictionaryByCode",
              type.dictCode,
            ),
            queryFn: () =>
              fetchDictionaryItems(type.dictCode ?? "", controller.signal),
            ...queryPolicies.live,
            staleTime: 0,
          })
        : [];
      if (
        controller.signal.aborted ||
        !activeRef.current ||
        !access() ||
        sessionRef.current.permissionEpoch !== identity.permissionEpoch ||
        inputRevisionRef.current !== inputRevision
      )
        throw new Error("读取或输入已变化，原编辑会话保持。 ");
      replace(type, createDictionaryItemRows(items), options.types);
      initializedRef.current = true;
    } catch (error) {
      // 主动隐藏/撤权取消只停止显示读取；保存结果分类完全不经过此分支。
      if (
        controller.signal.aborted ||
        !aliveRef.current ||
        !activeRef.current ||
        !access() ||
        sessionRef.current.permissionEpoch !== identity.permissionEpoch
      ) {
        if (
          !initializedRef.current &&
          !baselineRef.current &&
          !newTypeRef.current
        )
          initialAttemptedRef.current = false;
      } else {
        noticeRef.current = {
          type: "error",
          text: describeDictionaryReadError(error),
          persistent: true,
        };
      }
    } finally {
      if (readingRef.current === controller) {
        readingRef.current = null;
        phaseRef.current = "idle";
        publish();
        // 用户可能已在取消收尾前返回；补一次最新入口，避免活动 effect 已运行而漏掉初次恢复。
        if (!initialAttemptedRef.current) initializeRef.current();
      }
    }
  }
  /** 刷新先完整取类型和目标明细，再一次替换；业务读取失败绝不当空集接受。 */
  async function refresh(): Promise<void> {
    if (!(await authorizeReplacement())) return;
    if (!initializedRef.current && !baselineRef.current && !newTypeRef.current)
      initialAttemptedRef.current = true;
    const identity = queryIdentity();
    const inputRevision = inputRevisionRef.current;
    const controller = new AbortController();
    readingRef.current = controller;
    phaseRef.current = "loading";
    publish();
    try {
      const types = await queryClient.fetchQuery({
        queryKey: queryKeys.operation(identity, "getDictionaryTypes"),
        queryFn: () => fetchDictionaryTypes(controller.signal),
        ...queryPolicies.live,
        staleTime: 0,
      });
      const code = newTypeRef.current
        ? previousCodeRef.current
        : baselineRef.current?.dictCode;
      const selected =
        types.find((type) => type.dictCode === code) ?? types[0] ?? null;
      const items = selected
        ? await queryClient.fetchQuery({
            queryKey: queryKeys.operation(
              identity,
              "getDictionaryByCode",
              selected.dictCode,
            ),
            queryFn: () =>
              fetchDictionaryItems(selected.dictCode ?? "", controller.signal),
            ...queryPolicies.live,
            staleTime: 0,
          })
        : [];
      if (
        controller.signal.aborted ||
        !activeRef.current ||
        !access() ||
        sessionRef.current.permissionEpoch !== identity.permissionEpoch ||
        inputRevisionRef.current !== inputRevision
      )
        throw new Error("读取或输入已变化，原编辑会话保持。 ");
      initializedRef.current = true;
      replace(selected, createDictionaryItemRows(items), types);
    } catch (error) {
      if (
        controller.signal.aborted ||
        !aliveRef.current ||
        !activeRef.current ||
        !access() ||
        sessionRef.current.permissionEpoch !== identity.permissionEpoch
      ) {
        if (
          !initializedRef.current &&
          !baselineRef.current &&
          !newTypeRef.current
        )
          initialAttemptedRef.current = false;
      } else {
        noticeRef.current = {
          type: "error",
          text: describeDictionaryReadError(error),
          persistent: true,
        };
      }
    } finally {
      if (readingRef.current === controller) {
        readingRef.current = null;
        phaseRef.current = "idle";
        publish();
        if (!initialAttemptedRef.current) initializeRef.current();
      }
    }
  }
  /** 新类型只建立本地 Form 草稿，保存成功之前不允许为不存在的类型创建字典项。 */
  async function addType(): Promise<void> {
    if (!access("create") || !(await authorizeReplacement())) return;
    previousCodeRef.current = baselineRef.current?.dictCode ?? null;
    const type: DictionaryType = {
      dictCode: "",
      dictName: "",
      sortOrder:
        Math.max(0, ...typesRef.current.map((item) => item.sortOrder ?? 0)) +
        10,
      remark: "",
      parentId: null,
      groupCode: "",
      groupName: "",
      isSystem: false,
    };
    replace(type, []);
    initializedRef.current = true;
    newTypeRef.current = true;
    dataLoadedRef.current = false;
    publish();
  }
  function rowCanEdit(row: DictionaryItemRow): boolean {
    return (
      access(row.origin === "created" ? "create" : "edit") &&
      !typeDirty() &&
      !newTypeRef.current &&
      dataLoadedRef.current &&
      phaseRef.current === "idle"
    );
  }
  function cellText(row: DictionaryItemRow, field: ItemField): string {
    return (
      buffersRef.current.get(`${row.clientKey}:${field}`)?.raw ??
      String(row.current[field] ?? "")
    );
  }
  /** 控件变化进入原始缓冲；恢复原值只解除本格保护，不复制第二份业务 DTO。 */
  function changeCell(
    row: DictionaryItemRow,
    field: ItemField,
    raw: string,
  ): void {
    const latest = rowsRef.current.find(
      (item) => item.clientKey === row.clientKey,
    );
    if (!latest || latest.deleted || !rowCanEdit(latest)) return;
    const key = `${row.clientKey}:${field}`;
    const buffer: ItemBuffer = {
      clientKey: row.clientKey,
      field,
      raw,
      original: buffersRef.current.get(key)?.original ?? latest.current[field],
    };
    if (hasInputChanges(buffer, (text) => parseItem(field, text)))
      buffersRef.current.set(key, buffer);
    else buffersRef.current.delete(key);
    errorsRef.current.delete(key);
    publish();
  }
  function cancelCell(row: DictionaryItemRow, field: ItemField): void {
    if (phaseRef.current !== "idle" || !access()) return;
    buffersRef.current.delete(`${row.clientKey}:${field}`);
    errorsRef.current.delete(`${row.clientKey}:${field}`);
    publish();
  }
  /** 所有原文转换均通过后才一次进入编辑集合，必填/业务错误仍保留草稿且不发请求。 */
  function commitItems(): boolean {
    errorsRef.current.clear();
    for (const [key, buffer] of buffersRef.current)
      if (!parseItem(buffer.field, buffer.raw).valid)
        errorsRef.current.set(
          key,
          `${fieldTitles[buffer.field]}无效或尚未输入完成。`,
        );
    if (errorsRef.current.size) return false;
    for (const buffer of buffersRef.current.values()) {
      const parsed = parseItem(buffer.field, buffer.raw);
      if (parsed.valid)
        rowsRef.current = updateDictionaryItemRows(
          rowsRef.current,
          buffer.clientKey,
          { [buffer.field]: parsed.value },
        );
    }
    buffersRef.current.clear();
    for (const row of rowsRef.current.filter((item) => !item.deleted)) {
      const error = validateDictionaryItemValues(row.current);
      if (error) errorsRef.current.set(`${row.clientKey}:row`, error);
    }
    return errorsRef.current.size === 0;
  }
  /** 冻结范围逐操作授权；Create/Edit/Delete 不能以页面 View 或其他写权代替。 */
  function itemWriteAllowed(
    rows: readonly DictionaryItemRow[] = rowsRef.current,
  ): boolean {
    const changes = getEditableChanges(rows);
    return (
      access() &&
      (!changes.created.length || access("create")) &&
      (!changes.updated.length || access("edit")) &&
      (!changes.deleted.length ||
        (access("delete") &&
          changes.deleted.every((row) =>
            rows.some(
              (item) =>
                item.clientKey === row.clientKey && !item.source.isSystem,
            ),
          )))
    );
  }
  /** 类型与项共用准备锁，但每次只派发一个真实端点，回执及未知状态始终归原页签。 */
  async function save(
    kind: "type" | "items",
    operation: "save" | "delete" = "save",
  ): Promise<SaveDecision> {
    if (phaseRef.current !== "idle")
      return phaseRef.current === "unknown" ? "unknown" : "failed";
    if (!access()) {
      notice("warning", "当前身份或读取权限不允许保存，未发送请求。 ");
      return "failed";
    }
    if (kind === "type" && operation === "save" && !typeDirty()) return "saved";
    if (kind === "items" && !itemsDirty() && !hasBuffers()) return "saved";
    phaseRef.current = "preparing";
    recordRef.current = null;
    publish();
    try {
      await host.ensureVisible();
      if (!aliveRef.current || !access())
        throw new Error("展示或权限条件已改变，未发送请求。 ");
      let attempt: RecoveryAttempt;
      if (kind === "type") {
        const current = currentType();
        const verb =
          operation === "delete"
            ? "delete"
            : newTypeRef.current
              ? "create"
              : "edit";
        if (
          !current.value ||
          (operation !== "delete" && !current.valid) ||
          (operation !== "delete" && (itemsDirty() || hasBuffers())) ||
          isProtectedDictionaryType(baselineRef.current!) ||
          (newTypeRef.current && isProtectedDictionaryType(current.value)) ||
          !access(verb)
        )
          throw new Error(
            "类型字段、系统保护或操作权限不允许提交，未发送请求。 ",
          );
        if (operation !== "delete") {
          await form.validateFields();
          const error = validateDictionaryType(current.value);
          if (error) throw new Error(error);
        }
        attempt = {
          kind: "type",
          frozen: freezeDictionaryType({
            operation:
              operation === "delete"
                ? "delete"
                : newTypeRef.current
                  ? "create"
                  : "update",
            clientKey: typeClientKeyRef.current,
            current:
              operation === "delete" ? baselineRef.current! : current.value,
            baseline: newTypeRef.current ? undefined : baselineRef.current!,
          }),
        };
      } else {
        if (newTypeRef.current || typeDirty() || !dataLoadedRef.current)
          throw new Error("请先保存字典类型并成功读取字典项，未发送请求。 ");
        if (!commitItems())
          throw new Error(
            "字典项输入校验失败，原始输入与草稿保留，未发送请求。 ",
          );
        if (!itemWriteAllowed())
          throw new Error("本次增改删权限不足，未发送请求。 ");
        attempt = {
          kind: "items",
          frozen: freezeDictionaryItems(rowsRef.current),
        };
      }
      frozenRef.current = attempt;
      responseRef.current = null;
      phaseRef.current = "saving";
      publish();
      const result =
        attempt.kind === "type"
          ? await saveDictionaryType(attempt.frozen)
          : await saveDictionaryItems(attempt.frozen);
      if (
        !aliveRef.current ||
        sessionRef.current.identityEpoch !== ownerRef.current.identityEpoch ||
        sessionRef.current.user?.userId !== ownerRef.current.userId
      )
        return "unknown";
      responseRef.current = result;
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
      if ("acceptedType" in result) {
        const accepted = result.acceptedType;
        const old = baselineRef.current;
        if (!accepted) {
          typesRef.current = typesRef.current.filter(
            (type) => type.uid !== old?.uid,
          );
          replace(null, []);
        } else {
          typesRef.current = [
            ...typesRef.current.filter(
              (type) => type.uid !== old?.uid && type.uid !== accepted.uid,
            ),
            accepted,
          ];
          baselineRef.current = structuredClone(accepted);
          newTypeRef.current = false;
          typeClientKeyRef.current = accepted.uid ?? typeClientKeyRef.current;
          form.setFieldsValue(typeFormValues(accepted));
          if (old?.dictCode !== accepted.dictCode || !dataLoadedRef.current) {
            rowsRef.current = [];
            dataLoadedRef.current = false;
          }
        }
      } else rowsRef.current = [...result.acceptedRows];
      phaseRef.current = "idle";
      void invalidateDictionaryQueries(queryClient, queryIdentity()).catch(
        () => undefined,
      );
      notice("success", result.message);
      if (kind === "type" && baselineRef.current && !dataLoadedRef.current)
        await loadTarget(baselineRef.current, { guard: false });
      return kind === "items" && (itemsDirty() || hasBuffers())
        ? "unsaved"
        : "saved";
    } catch (error) {
      if (!aliveRef.current) return "unknown";
      if (phaseRef.current === "saving") {
        phaseRef.current = "unknown";
        responseRef.current = { message: "派发后无法核对完整结果。" };
        notice("error", "派发后的异常不能证明未生效，保留恢复限制。 ");
        return "unknown";
      }
      phaseRef.current = "idle";
      notice("error", describeApiError(error));
      return "failed";
    }
  }
  /** Workspace 保存依次处理类型和项；第一阶段失败或未知立即停止，不伪造两端点事务。 */
  async function requestSave(): Promise<SaveDecision> {
    if (phaseRef.current !== "idle")
      return phaseRef.current === "unknown" ? "unknown" : "failed";
    if (typeDirty()) {
      const result = await save("type");
      if (result !== "saved") return result;
    }
    if (itemsDirty() || hasBuffers()) {
      const result = await save("items");
      if (result !== "saved") return result;
    }
    return isUnprotected(getSummary()) ? "saved" : "unsaved";
  }
  /** 普通放弃仅恢复本会话基线，准备/保存/未知状态及失权内容不能借此释放。 */
  async function requestDiscard(): Promise<DiscardDecision> {
    if (phaseRef.current !== "idle" || !access()) return "blocked";
    if (newTypeRef.current) {
      replace(null, []);
    } else {
      if (baselineRef.current)
        form.setFieldsValue(typeFormValues(baselineRef.current));
      rowsRef.current = discardDictionaryItemRows(rowsRef.current);
      buffersRef.current.clear();
      errorsRef.current.clear();
      noticeRef.current = null;
    }
    publish();
    return "discarded";
  }
  async function discard(scope: "type" | "items"): Promise<void> {
    if (phaseRef.current !== "idle" || !access()) return;
    const accepted = await modal.confirm({
      title: scope === "type" ? "放弃类型变更" : "放弃字典项变更",
      content: "恢复该部分已确认基线，清除对应未提交输入。",
      okText: "放弃",
      cancelText: "取消",
    });
    if (!accepted || phaseRef.current !== "idle" || !access()) return;
    if (scope === "type") {
      if (newTypeRef.current) replace(null, []);
      else if (baselineRef.current)
        form.setFieldsValue(typeFormValues(baselineRef.current));
    } else {
      rowsRef.current = discardDictionaryItemRows(rowsRef.current);
      buffersRef.current.clear();
      errorsRef.current.clear();
    }
    noticeRef.current = null;
    publish();
  }
  /** 删除类型需保护全部输入并二次确认；新类型取消只移除本地草稿，不发删除请求。 */
  async function deleteType(): Promise<void> {
    if (newTypeRef.current) {
      await discard("type");
      return;
    }
    if (
      !access("delete") ||
      !baselineRef.current ||
      isProtectedDictionaryType(baselineRef.current) ||
      !(await authorizeReplacement())
    )
      return;
    const accepted = await modal.confirm({
      title: "删除字典类型",
      content: "有字典项或子类型时服务器会拒绝删除。删除未确认前保留原会话。",
      okText: "删除",
      cancelText: "取消",
      okButtonProps: { danger: true },
    });
    if (accepted && phaseRef.current === "idle" && access("delete"))
      await save("type", "delete");
  }
  function addItem(): void {
    if (
      !access("create") ||
      phaseRef.current !== "idle" ||
      typeDirty() ||
      !dataLoadedRef.current ||
      !baselineRef.current?.dictCode
    )
      return;
    const row = createDictionaryItemRow(baselineRef.current.dictCode);
    rowsRef.current = updateDictionaryItemRows(
      [...rowsRef.current, row],
      row.clientKey,
      {
        sortOrder:
          Math.max(
            0,
            ...rowsRef.current
              .filter((item) => !item.deleted)
              .map((item) => item.current.sortOrder ?? 0),
          ) + 10,
      },
    );
    publish();
  }
  function canDeleteRow(row: DictionaryItemRow): boolean {
    return (
      !row.source.isSystem &&
      (row.origin === "created" ? access() : access("delete"))
    );
  }
  /** 标记既有删除供撤销；新增行直接撤销，系统项及失权既有行保持原状。 */
  function deleteItems(): void {
    if (phaseRef.current !== "idle" || typeDirty()) return;
    for (const row of rowsRef.current)
      if (selectedRef.current.includes(row.clientKey) && canDeleteRow(row)) {
        rowsRef.current = removeDictionaryItemRows(
          rowsRef.current,
          row.clientKey,
        );
        for (const field of itemFields)
          buffersRef.current.delete(`${row.clientKey}:${field}`);
      }
    selectedRef.current = [];
    publish();
  }
  function restoreItems(): void {
    if (phaseRef.current !== "idle" || !access()) return;
    for (const row of rowsRef.current)
      if (row.deleted)
        rowsRef.current = restoreDictionaryItemRows(
          rowsRef.current,
          row.clientKey,
        );
    publish();
  }
  /** 区域填充逐列重定位和核权，全部源合法才向当前选区目标写入短期输入缓冲。 */
  function fillDown(
    intent: SwiftDataGridFillDownIntent<DictionaryItemRow>,
  ): void {
    if (phaseRef.current !== "idle" || !access()) return;
    const patches: { row: DictionaryItemRow; field: ItemField; raw: string }[] =
      [];
    for (const column of intent.columns) {
      if (
        !itemFields.includes(column.columnKey as ItemField) ||
        column.fields.length !== 1 ||
        column.fields[0] !== column.columnKey
      )
        return;
      const field = column.columnKey as ItemField;
      const source = rowsRef.current.find(
        (row) => row.clientKey === column.source.clientKey && !row.deleted,
      );
      if (!source || !rowCanEdit(source)) return;
      const raw = cellText(source, field);
      if (
        !parseItem(field, raw).valid ||
        ((field === "dataNameEn" || field === "dataNameZh") && !raw.trim())
      ) {
        notice("warning", "填充源无效，请先完成该单元格输入。 ");
        return;
      }
      for (const target of column.targets) {
        const row = rowsRef.current.find(
          (item) => item.clientKey === target.clientKey && !item.deleted,
        );
        if (!row || !rowCanEdit(row)) return;
        patches.push({ row, field, raw });
      }
    }
    for (const patch of patches) changeCell(patch.row, patch.field, patch.raw);
  }
  /** 导出及人工终止重新核验冻结操作写权，撤权后不能导出旧内容绕过访问限制。 */
  function recoveryAllowed(): boolean {
    const attempt = frozenRef.current;
    if (!access() || !attempt) return false;
    return attempt.kind === "items"
      ? itemWriteAllowed(attempt.frozen.rows)
      : access(
          attempt.frozen.operation === "create"
            ? "create"
            : attempt.frozen.operation === "update"
              ? "edit"
              : "delete",
        );
  }
  /** 白名单恢复 JSON 保全必要请求与事实；文件下载触发本身不作为可靠保全证明。 */
  function exportRecovery(): void {
    const attempt = frozenRef.current;
    if (phaseRef.current !== "unknown" || !attempt || !recoveryAllowed()) {
      notice("warning", "当前权限不允许保全，需现有合法业务/运维渠道介入。 ");
      return;
    }
    recordRef.current = null;
    let url: string | undefined;
    try {
      const recordId = crypto.randomUUID();
      const record = createDictionaryRecoveryRecord(
        recordId,
        attempt.frozen,
        responseRef.current ?? { message: "服务器最终结果仍未确认。" },
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
      notice("error", "导出失败，继续保留恢复限制。 ");
    } finally {
      if (url) URL.revokeObjectURL(url);
    }
  }
  /** 用户先确认记录已妥善保留，再确认释放本地草稿；任何取消、撤权或状态变化都保持未知锁。 */
  async function terminateRecovery(): Promise<void> {
    const record = recordRef.current;
    if (phaseRef.current !== "unknown" || !record || !recoveryAllowed()) {
      notice("warning", "未完成合法导出，不能终止本地恢复。 ");
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
      notice("warning", "宿主未接受人工终止，恢复限制仍保留。 ");
  }

  controlsRef.current = {
    getSummary,
    canClose: () =>
      isUnprotected(getSummary())
        ? { allowed: true }
        : {
            allowed: false,
            reason: "字典修改、未提交输入或保存恢复尚未处理。",
          },
    requestSave,
    requestDiscard,
  };
  initializeRef.current = () => {
    const data = typesQuery.data;
    if (
      initializedRef.current ||
      initialAttemptedRef.current ||
      baselineRef.current ||
      newTypeRef.current ||
      !data ||
      !isActive ||
      !canRead ||
      phaseRef.current !== "idle"
    )
      return;
    initialAttemptedRef.current = true;
    typesRef.current = [...data];
    publish();
    if (data[0]) void loadTarget(data[0], { guard: false });
    else initializedRef.current = true;
  };
  useEffect(() => {
    initializeRef.current();
  }, [typesQuery.data, isActive, canRead]);
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
      readingRef.current?.abort();
    };
  }, []);
  useEffect(() => {
    if (!isActive || !canRead) readingRef.current?.abort();
    host.reportSummary(controlsRef.current!.getSummary());
  }, [isActive, canRead, session.permissionEpoch, host]);

  const locked = phaseRef.current !== "idle";
  const protectedType =
    !!baselineRef.current && isProtectedDictionaryType(baselineRef.current);
  const itemsProtected = itemsDirty() || hasBuffers();
  const columns: SwiftDataGridColumn<DictionaryItemRow>[] = itemFields.map(
    (field) => {
      const feedback = {
        getLabel: (_row: DictionaryItemRow, index: number) =>
          `字典项${index + 1}${fieldTitles[field]}`,
        getError: (row: DictionaryItemRow) =>
          errorsRef.current.get(`${row.clientKey}:${field}`) ??
          errorsRef.current.get(`${row.clientKey}:row`),
        onCancel: (row: DictionaryItemRow) => cancelCell(row, field),
      };
      return field === "isEnabled"
        ? {
            key: field,
            title: fieldTitles[field],
            width: 90,
            allowFillDown: true,
            editor: {
              kind: "custom",
              ...feedback,
              render: ({ row, readOnly, inputAttributes }) => (
                <Checkbox
                  {...inputAttributes}
                  checked={cellText(row, field) === "true"}
                  disabled={readOnly}
                  onChange={(event) =>
                    changeCell(row, field, String(event.target.checked))
                  }
                />
              ),
            },
          }
        : {
            key: field,
            title: fieldTitles[field],
            width: field === "sortOrder" ? 100 : field === "remark" ? 260 : 190,
            editor: {
              ...feedback,
              inputMode: field === "sortOrder" ? "numeric" : undefined,
              getText: (row) => cellText(row, field),
              onChange: (row, raw) => changeCell(row, field, raw),
            },
          };
    },
  );
  columns.push({
    key: "isSystem",
    title: "系统项",
    width: 90,
    readOnly: true,
    render: (row) => (row.source.isSystem ? "是" : "否"),
  });
  return {
    tabId: descriptor.tabId,
    form,
    types: typesRef.current,
    selectedType: baselineRef.current,
    isNewType: newTypeRef.current,
    protectedType,
    phase: phaseRef.current,
    summary: getSummary(),
    notice: noticeRef.current,
    queryError: typesQuery.error
      ? describeDictionaryReadError(typesQuery.error)
      : null,
    loading: typesQuery.isPending || phaseRef.current === "loading",
    typeReadOnly:
      locked ||
      protectedType ||
      itemsProtected ||
      !access(newTypeRef.current ? "create" : "edit"),
    itemsReadOnly:
      locked || !dataLoadedRef.current || typeDirty() || newTypeRef.current,
    rows: rowsRef.current.filter((row) => !row.deleted),
    columns,
    selectedKeys: selectedRef.current,
    canCreateType: !locked && access("create"),
    canSaveType:
      !locked &&
      !itemsProtected &&
      !protectedType &&
      typeDirty() &&
      access(newTypeRef.current ? "create" : "edit"),
    canDeleteType:
      !locked &&
      !itemsProtected &&
      !!baselineRef.current &&
      !protectedType &&
      (newTypeRef.current || access("delete")),
    canDiscardType: !locked && typeDirty(),
    canCreateItem:
      !locked && !typeDirty() && dataLoadedRef.current && access("create"),
    canSaveItems:
      !locked &&
      !typeDirty() &&
      (itemsDirty() || hasBuffers()) &&
      itemWriteAllowed(),
    canDeleteItems:
      !locked &&
      !typeDirty() &&
      rowsRef.current.some(
        (row) =>
          selectedRef.current.includes(row.clientKey) && canDeleteRow(row),
      ),
    canRestoreItems:
      !locked && access() && rowsRef.current.some((row) => row.deleted),
    canDiscardItems: !locked && itemsProtected,
    selectType: (type: DictionaryType) => {
      if (!newTypeRef.current && baselineRef.current?.uid === type.uid) return;
      void loadTarget(type);
    },
    addType,
    refresh,
    saveType: () => save("type"),
    saveItems: () => save("items"),
    deleteType,
    discardType: () => discard("type"),
    discardItems: () => discard("items"),
    addItem,
    deleteItems,
    restoreItems,
    onTypeValuesChange: () => {
      inputRevisionRef.current += 1;
      publish();
    },
    rowCanEdit,
    onSelectionChange: (keys: SwiftDataGridRowKey[]) => {
      selectedRef.current = keys;
      publish();
    },
    fillDown,
    exportRecovery,
    terminateRecovery,
    itemsNotice: itemsProtected ? (
      <Alert
        type="info"
        title="字典项有未保存输入，请先保存或放弃后再修改类型属性。"
      />
    ) : null,
  };
}
