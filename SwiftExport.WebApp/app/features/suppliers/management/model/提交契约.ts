import type {
  SwiftExportContractsDTOsSuppliersRequestsSupplierBatchSaveRequest,
  SwiftExportContractsWrappersSaveItemResult,
} from "../../../../shared/api/generated/client";
import type { RequestFacts } from "../../../../shared/api/请求执行";
import {
  createEditableRow,
  getEditableChanges,
} from "../../../../shared/editing/编辑集合";
import {
  isSupplierUid,
  isSupplierVersion,
  projectSupplierValues,
  validateSupplierValues,
} from "./供应商模型";
import type { SupplierDto, SupplierRow } from "./供应商模型";

/** 批量传输结构只引用生成类型，本地 attemptId 不写入服务器不存在的幂等字段。 */
export type SupplierBatchRequest =
  SwiftExportContractsDTOsSuppliersRequestsSupplierBatchSaveRequest;

/** 冻结集合、请求与时间只属于本次提交，等待回执时不会读取后来输入。 */
export interface FrozenSuppliers {
  readonly rows: readonly SupplierRow[];
  readonly request: SupplierBatchRequest;
  readonly attemptId: string;
  readonly submittedAt: string;
}

/** 生成器未列出所有失败状态，feature 仍保留真实 HTTP 正文与状态。 */
export interface SupplierHttpResponse {
  readonly status: number;
  readonly data: unknown;
  readonly headers: Headers;
}

/** 展示说明与提交结论分开，恢复入口消费同一冻结请求和传输事实。 */
export interface SupplierSaveEvidence {
  readonly message: string;
  readonly response?: SupplierHttpResponse;
  readonly facts?: RequestFacts;
}

/** 供应商只接受整批原子成功；确定未发送为 B，其他未确认结果保留 C 类保护。 */
export type SupplierSaveResult = SupplierSaveEvidence &
  (
    | {
        readonly kind: "confirmedSuccess";
        readonly acceptedRows: SupplierRow[];
      }
    | { readonly kind: "confirmedNoEffect" }
    | { readonly kind: "unknown" }
  );

/** 由唯一集合派生增改删，校验后冻结；修改和删除使用原服务器身份与版本。 */
export function freezeSupplierRows(
  rows: readonly SupplierRow[],
): FrozenSuppliers {
  const seenKeys = new Set<string>();
  const seenUids = new Set<string>();
  for (const row of rows) {
    if (!row.clientKey.trim() || seenKeys.has(row.clientKey))
      throw new Error("供应商提交缺少唯一 ClientKey。");
    seenKeys.add(row.clientKey);
    if (row.origin !== "created") {
      requireExistingIdentity(row.uid, row.version);
      if (seenUids.has(row.uid.toLowerCase()))
        throw new Error("供应商编辑集合包含重复服务器身份。");
      seenUids.add(row.uid.toLowerCase());
    }
  }
  const changes = getEditableChanges(rows);
  if (
    !changes.created.length &&
    !changes.updated.length &&
    !changes.deleted.length
  )
    throw new Error("没有需要保存的供应商资料。");
  const byKey = new Map(rows.map((row) => [row.clientKey, row]));
  const codes = new Set<string>();
  for (const row of [...changes.created, ...changes.updated]) {
    const errors = validateSupplierValues(row.current);
    const error = Object.values(errors)[0];
    if (error)
      throw new Error(
        `第 ${rows.indexOf(byKey.get(row.clientKey)!) + 1} 行：${error}`,
      );
    const code = row.current.supplierCode.trim().toLowerCase();
    if (codes.has(code)) throw new Error("本次提交中存在重复供应商代码。");
    codes.add(code);
  }
  const request: SupplierBatchRequest = {
    createItems: changes.created.map((row) => ({
      clientKey: row.clientKey,
      rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
      data: projectSubmission(byKey.get(row.clientKey)!, true),
    })),
    updateItems: changes.updated.map((row) => ({
      clientKey: row.clientKey,
      rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
      uid: row.uid,
      version: row.version,
      data: projectSubmission(byKey.get(row.clientKey)!, false),
    })),
    deleteItems: changes.deleted.map((row) => ({
      clientKey: row.clientKey,
      rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
      uid: row.uid,
      version: row.version,
      isSoftDelete: true,
    })),
  };
  // 只复制 JSON 状态；可选的纯比较函数保持引用，避免 structuredClone 拒绝函数。
  const frozenRows = rows.map((row) => ({
    ...row,
    baseline: structuredClone(row.baseline),
    current: structuredClone(row.current),
    source: structuredClone(row.source),
  }));
  return deepFreeze({
    rows: frozenRows,
    request,
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
  });
}

/** 完整核对整批后才接纳，新增采用服务器身份，删除移除且保留未提交行。 */
export function acceptSupplierBatch(
  frozen: FrozenSuppliers,
  envelope: unknown,
): SupplierRow[] {
  const receipts = requireCompleteReceipt(frozen, envelope);
  const byKey = new Map(
    receipts.map((receipt) => [receipt.clientKey, receipt]),
  );
  const submissions = new Map(
    [
      ...(frozen.request.createItems ?? []),
      ...(frozen.request.updateItems ?? []),
    ].map((item) => [item.clientKey, item.data]),
  );
  return frozen.rows.flatMap((row) => {
    const receipt = byKey.get(row.clientKey);
    if (!receipt)
      return [
        {
          ...row,
          baseline: structuredClone(row.baseline),
          current: structuredClone(row.current),
          source: structuredClone(row.source),
        },
      ];
    if (receipt.operation === "Delete") return [];
    const submitted = submissions.get(row.clientKey);
    if (!submitted) throw new Error("供应商回执缺少对应冻结 DTO。");
    const source: SupplierDto = {
      ...structuredClone(submitted),
      uid: receipt.uid!,
      version: receipt.newVersion!,
      isDeleted: false,
    };
    return [
      {
        ...createEditableRow(
          row.clientKey,
          receipt.uid!,
          receipt.newVersion!,
          projectSupplierValues(source),
        ),
        source,
      },
    ];
  });
}

/** 恢复材料仅导出核实白名单，不包含认证头、原始正文或凭空构造的 CommandId。 */
export function createSupplierRecoveryRecord(
  recordId: string,
  frozen: FrozenSuppliers,
  evidence: SupplierSaveEvidence,
) {
  return {
    recordId,
    businessType: "supplier-management",
    interfaceOperation: "POST /api/supplier/batch-save",
    submittedAt: frozen.submittedAt,
    attemptId: frozen.attemptId,
    attemptIdMeaning: "仅用于本地关联，不是服务器幂等键",
    frozenSubmission: exportRequest(frozen.request),
    response: exportResponse(evidence.response),
    error: { message: evidence.message },
    trace: evidence.facts
      ? {
          phase: evidence.facts.phase,
          status: evidence.facts.status,
          correlationId: evidence.facts.correlationId,
        }
      : null,
    pendingQuestions: [
      "服务器是否提交本次供应商批量操作仍未确认。",
      "人工终止仅释放本地草稿，不证明失败、回滚或可安全重发。",
    ],
  };
}

/** 只提交当前真实可写字段和并发元数据，三项必填文本与服务器 Trim 保持一致。 */
function projectSubmission(row: SupplierRow, created: boolean): SupplierDto {
  return {
    ...projectSupplierValues(row.current),
    uid: created ? "00000000-0000-0000-0000-000000000000" : row.uid,
    version: created ? 0 : row.version,
    isDeleted: false,
  };
}

/** 已有操作不接受本地临时身份或零版本，服务器仍负责检查实际存在与并发。 */
function requireExistingIdentity(uid: string, version: number): void {
  if (!isSupplierUid(uid) || !isSupplierVersion(version))
    throw new Error("供应商修改或删除缺少有效服务器身份和基线版本。");
}

/** 该原子端点全部回执必须匹配冻结请求，不能依赖 AllSucceeded 对空 Items 的默认真值。 */
function requireCompleteReceipt(
  frozen: FrozenSuppliers,
  envelope: unknown,
): SwiftExportContractsWrappersSaveItemResult[] {
  if (
    !isRecord(envelope) ||
    envelope.success !== true ||
    envelope.error != null ||
    !isRecord(envelope.data)
  )
    throw new Error("供应商保存外层成功契约不完整。");
  const batch = envelope.data;
  const request = frozen.request;
  const expected = [
    ...(request.createItems ?? []).map((item) => ({
      ...item,
      operation: "Create",
    })),
    ...(request.updateItems ?? []).map((item) => ({
      ...item,
      operation: "Update",
    })),
    ...(request.deleteItems ?? []).map((item) => ({
      ...item,
      operation: "Delete",
    })),
  ];
  if (
    !expected.length ||
    !Array.isArray(batch.items) ||
    batch.items.length !== expected.length ||
    batch.totalCount !== expected.length ||
    batch.successCount !== expected.length ||
    batch.failedCount !== 0 ||
    batch.allSucceeded !== true ||
    batch.addSuccessCount !== (request.createItems?.length ?? 0) ||
    batch.updateSuccessCount !== (request.updateItems?.length ?? 0) ||
    batch.deleteSuccessCount !== (request.deleteItems?.length ?? 0)
  )
    throw new Error("供应商保存回执遗漏或计数不一致，不能接纳。");
  const seenKeys = new Set<string>();
  const seenUids = new Set<string>();
  const existingUids = new Set(
    frozen.rows
      .filter((row) => row.origin !== "created")
      .map((row) => row.uid.toLowerCase()),
  );
  return batch.items.map((value: unknown) => {
    if (
      !isRecord(value) ||
      typeof value.clientKey !== "string" ||
      seenKeys.has(value.clientKey) ||
      !isSupplierUid(value.uid) ||
      seenUids.has(value.uid.toLowerCase())
    )
      throw new Error("供应商保存回执身份缺失或重复。");
    seenKeys.add(value.clientKey);
    seenUids.add(value.uid.toLowerCase());
    const submitted = expected.find(
      (item) => item.clientKey === value.clientKey,
    );
    if (
      !submitted ||
      submitted.operation !== value.operation ||
      submitted.rowIndex !== value.rowIndex ||
      value.success !== true ||
      value.errorCode != null
    )
      throw new Error("供应商保存回执与冻结操作不一致。");
    if (submitted.operation === "Create") {
      if (
        value.oldVersion != null ||
        value.newVersion !== 1 ||
        existingUids.has(value.uid.toLowerCase())
      )
        throw new Error("供应商新增回执没有可靠服务器身份或版本。");
    } else {
      if (
        !("uid" in submitted) ||
        !("version" in submitted) ||
        submitted.uid?.toLowerCase() !== value.uid.toLowerCase()
      )
        throw new Error("供应商修改或删除回执实体身份不匹配。");
      // 当前 PG UpdateAsync 提高实体 Version 后再返回 OldVersion；软删不提高 Version。
      const expectedVersion =
        submitted.operation === "Update"
          ? submitted.version! + 1
          : submitted.version!;
      if (
        value.newVersion !== expectedVersion ||
        value.oldVersion !== expectedVersion ||
        !isSupplierVersion(value.newVersion)
      )
        throw new Error("供应商保存回执版本与当前端点契约不匹配。");
    }
    return value as SwiftExportContractsWrappersSaveItemResult;
  });
}

/** 深冻结已复制的 JSON 状态，不共享调用方可写对象或冻结其原始草稿。 */
function deepFreeze<T>(value: T): T {
  if (typeof value === "object" && value !== null) {
    for (const child of Object.values(value)) deepFreeze(child);
    Object.freeze(value);
  }
  return value;
}

/** 正文运行时必须是对象，数组与空值不能充当契约包装。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** 请求核实白名单不随 DTO 的未来扩展自动带出其他字段。 */
function exportRequest(request: SupplierBatchRequest) {
  const data = (value: SupplierDto | null | undefined) =>
    value
      ? {
          uid: value.uid,
          version: value.version,
          isDeleted: value.isDeleted,
          ...projectSupplierValues(value),
        }
      : null;
  return {
    createItems: request.createItems?.map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      data: data(item.data),
    })),
    updateItems: request.updateItems?.map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      uid: item.uid,
      version: item.version,
      data: data(item.data),
    })),
    deleteItems: request.deleteItems?.map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      uid: item.uid,
      version: item.version,
      isSoftDelete: item.isSoftDelete,
    })),
  };
}

/** 响应只保留结构化错误、计数和行回执，Headers 与 rawBody 不进入导出。 */
function exportResponse(response: SupplierHttpResponse | undefined) {
  if (!response) return null;
  const envelope = isRecord(response.data) ? response.data : {};
  const batch = isRecord(envelope.data) ? envelope.data : {};
  const error = isRecord(envelope.error) ? envelope.error : {};
  return {
    status: response.status,
    success: envelope.success,
    error: { code: error.code, message: error.message, field: error.field },
    data: {
      totalCount: batch.totalCount,
      successCount: batch.successCount,
      failedCount: batch.failedCount,
      allSucceeded: batch.allSucceeded,
      addSuccessCount: batch.addSuccessCount,
      updateSuccessCount: batch.updateSuccessCount,
      deleteSuccessCount: batch.deleteSuccessCount,
      items: Array.isArray(batch.items)
        ? batch.items.map((item: unknown) =>
            isRecord(item)
              ? {
                  clientKey: item.clientKey,
                  rowIndex: item.rowIndex,
                  operation: item.operation,
                  uid: item.uid,
                  success: item.success,
                  oldVersion: item.oldVersion,
                  newVersion: item.newVersion,
                  errorCode: item.errorCode,
                  message: item.message,
                }
              : null,
          )
        : null,
    },
  };
}
