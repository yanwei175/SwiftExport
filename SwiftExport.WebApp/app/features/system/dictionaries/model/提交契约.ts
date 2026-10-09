import type {
  SwiftExportContractsDTOsSystemDtosRequestsSysDictTypeBatchSaveRequest,
  SwiftExportContractsDTOsSystemDtosRequestsSysDictDataBatchSaveRequest,
  SwiftExportContractsWrappersSaveItemResult,
} from "../../../../shared/api/generated/client";
import {
  getEditableChanges,
  createEditableRow,
} from "../../../../shared/editing/编辑集合";
import type { RequestFacts } from "../../../../shared/api/请求执行";
import {
  isDictionaryUid,
  isDictionaryVersion,
  isProtectedDictionaryType,
  isSystemDictionaryCode,
  projectDictionaryItemValues,
  validateDictionaryType,
  validateDictionaryItemValues,
} from "./字典模型";
import type {
  DictionaryType,
  DictionaryItem,
  DictionaryItemRow,
  DictionaryItemValues,
} from "./字典模型";

/** 所有请求类型直接复用生成产物；本地尝试标识不写入不存在的后端幂等字段。 */
export type DictionaryTypeBatchRequest =
  SwiftExportContractsDTOsSystemDtosRequestsSysDictTypeBatchSaveRequest;
/** 字典项仍使用自己的真实 BatchSave 契约，不能套用产品命令 DTO。 */
export type DictionaryItemsBatchRequest =
  SwiftExportContractsDTOsSystemDtosRequestsSysDictDataBatchSaveRequest;
/** 字典类型 Form 每次提交一个明确操作，创建和删除不混入另一端点的事务。 */
export type DictionaryTypeOperation = "create" | "update" | "delete";

/** 类型冻结材料保留完整 DTO 和原并发身份；操作由 feature 在提交前确定。 */
export interface FrozenDictionaryType {
  readonly kind: "type";
  readonly attemptId: string;
  readonly submittedAt: string;
  readonly operation: DictionaryTypeOperation;
  readonly clientKey: string;
  readonly current: DictionaryType;
  readonly baseline?: DictionaryType;
  readonly request: DictionaryTypeBatchRequest;
}

/** 原子字典项提交冻结整个来源集合，回执接纳不读取后来输入或别的页签。 */
export interface FrozenDictionaryItems {
  readonly kind: "items";
  readonly attemptId: string;
  readonly submittedAt: string;
  readonly rows: readonly DictionaryItemRow[];
  readonly request: DictionaryItemsBatchRequest;
}

/** 生成器只描述了 200，但统一执行边界仍会返回其他实际状态，feature 必须按事实解释。 */
export interface DictionaryHttpResponse {
  readonly status: number;
  readonly data: unknown;
  readonly headers: Headers;
}

/** 保存证据保留事实与显示说明；UI 自行持有同一 frozen，不能因消息写着失败就解锁。 */
export interface DictionarySaveEvidence {
  readonly message: string;
  readonly response?: DictionaryHttpResponse;
  readonly facts?: RequestFacts;
}

/** A 必须完整接纳；B 保留草稿可修正；C 保留冻结材料进入现有未知恢复。 */
export type DictionaryTypeSaveResult = DictionarySaveEvidence &
  (
    | {
        readonly kind: "confirmedSuccess";
        readonly acceptedType: DictionaryType | null;
      }
    | { readonly kind: "confirmedNoEffect" }
    | { readonly kind: "unknown" }
  );

/** 此端点只有整批 A，不以部分行成功释放其他行；异常混合回执属于 C。 */
export type DictionaryItemsSaveResult = DictionarySaveEvidence &
  (
    | {
        readonly kind: "confirmedSuccess";
        readonly acceptedRows: DictionaryItemRow[];
      }
    | { readonly kind: "confirmedNoEffect" }
    | { readonly kind: "unknown" }
  );

/** 读取 Form 最新值后调用；校验失败抛出本地错误，不会调用生成请求。 */
export function freezeDictionaryType(input: {
  operation: DictionaryTypeOperation;
  clientKey: string;
  current: DictionaryType;
  baseline?: DictionaryType;
}): FrozenDictionaryType {
  if (!input.clientKey.trim()) throw new Error("类型提交缺少 ClientKey。");
  const current = normalizeType(input.current);
  const baseline = input.baseline ? structuredClone(input.baseline) : undefined;
  if (input.operation === "create") {
    current.uid = isDictionaryUid(current.uid)
      ? current.uid
      : crypto.randomUUID();
    current.version = 0;
    current.isSystem = false;
  } else {
    if (
      !baseline ||
      !isDictionaryUid(baseline.uid) ||
      !isDictionaryVersion(baseline.version)
    )
      throw new Error("类型修改或删除缺少原服务器身份和版本。");
    current.uid = baseline.uid;
    current.version = baseline.version;
    current.isSystem = baseline.isSystem;
    if (input.operation === "delete" && isProtectedDictionaryType(baseline))
      throw new Error("系统字典类型不能删除。");
    if (
      input.operation === "update" &&
      isSystemDictionaryCode(baseline.dictCode) &&
      current.dictCode?.toLowerCase() !==
        baseline.dictCode?.trim().toLowerCase()
    )
      throw new Error("系统字典编码不能修改。");
  }
  if (input.operation !== "delete") {
    const error = validateDictionaryType(current);
    if (error) throw new Error(error);
  }
  const row = { clientKey: input.clientKey, rowIndex: 1 };
  const request: DictionaryTypeBatchRequest = {
    createItems:
      input.operation === "create" ? [{ ...row, data: current }] : [],
    updateItems:
      input.operation === "update"
        ? [
            {
              ...row,
              uid: baseline!.uid,
              version: baseline!.version,
              data: current,
            },
          ]
        : [],
    deleteItems:
      input.operation === "delete"
        ? [
            {
              ...row,
              uid: baseline!.uid,
              version: baseline!.version,
              isSoftDelete: true,
            },
          ]
        : [],
  };
  return freezeClone<FrozenDictionaryType>({
    ...input,
    current,
    baseline,
    kind: "type",
    request,
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
  });
}

/** 由同一集合派生增改删，先验证整批输入后冻结；来源 DTO 中未展示字段不丢失。 */
export function freezeDictionaryItems(
  rows: readonly DictionaryItemRow[],
): FrozenDictionaryItems {
  const changes = getEditableChanges(rows);
  if (
    !changes.created.length &&
    !changes.updated.length &&
    !changes.deleted.length
  )
    throw new Error("没有需要保存的字典项。");
  const byKey = new Map(rows.map((row) => [row.clientKey, row]));
  const request: DictionaryItemsBatchRequest = {
    createItems: changes.created.map((row) => ({
      clientKey: row.clientKey,
      rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
      data: projectSubmittedItem(byKey.get(row.clientKey)!, true),
    })),
    updateItems: changes.updated.map((row) => {
      requireExistingIdentity(row.uid, row.version);
      return {
        clientKey: row.clientKey,
        rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
        uid: row.uid,
        version: row.version,
        data: projectSubmittedItem(byKey.get(row.clientKey)!, false),
      };
    }),
    deleteItems: changes.deleted.map((row) => {
      requireExistingIdentity(row.uid, row.version);
      if (byKey.get(row.clientKey)!.source.isSystem === true)
        throw new Error("系统字典项不能删除。");
      return {
        clientKey: row.clientKey,
        rowIndex: rows.indexOf(byKey.get(row.clientKey)!) + 1,
        uid: row.uid,
        version: row.version,
        isSoftDelete: true,
      };
    }),
  };
  const dictionaryCodes = new Set(
    rows.map((row) => row.current.dictCode.trim().toLowerCase()),
  );
  if (dictionaryCodes.size > 1)
    throw new Error("同一字典项编辑会话不能混用多个字典编码。");
  return freezeClone<FrozenDictionaryItems>({
    kind: "items",
    rows,
    request,
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
  });
}

/** 全部回执通过后才形成新类型基线，创建采用真实服务器 Uid，删除返回 null。 */
export function acceptDictionaryTypeBatch(
  frozen: FrozenDictionaryType,
  envelope: unknown,
): DictionaryType | null {
  const receipts = requireCompleteReceipt(frozen.request, envelope);
  if (frozen.operation === "delete") return null;
  const receipt = receipts[0]!;
  return {
    ...structuredClone(frozen.current),
    uid: receipt.uid!,
    version: receipt.newVersion!,
    isSystem:
      frozen.current.isSystem === true ||
      isSystemDictionaryCode(frozen.current.dictCode),
    isDeleted: false,
  };
}

/** 不在完整核验前清除任一草稿；未修改行保留，删除接纳仅移除对应行。 */
export function acceptDictionaryItemsBatch(
  frozen: FrozenDictionaryItems,
  envelope: unknown,
): DictionaryItemRow[] {
  const receipts = requireCompleteReceipt(frozen.request, envelope);
  const receiptByKey = new Map(receipts.map((item) => [item.clientKey, item]));
  const submittedByKey = new Map(
    [
      ...(frozen.request.createItems ?? []),
      ...(frozen.request.updateItems ?? []),
    ].map((item) => [item.clientKey, item.data]),
  );
  return frozen.rows.flatMap((row) => {
    const receipt = receiptByKey.get(row.clientKey);
    if (!receipt) return [structuredClone(row)];
    if (receipt.operation === "Delete") return [];
    const data = submittedByKey.get(row.clientKey);
    if (!data) throw new Error("字典项回执缺少对应冻结 DTO。");
    const source: DictionaryItem = {
      ...structuredClone(data),
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
          projectDictionaryItemValues(source),
        ),
        source,
      },
    ];
  });
}

/** 恢复导出仅保留核实必需 DTO/错误和追踪白名单；不复制 Headers、原始正文或凭据。 */
export function createDictionaryRecoveryRecord(
  recordId: string,
  frozen: FrozenDictionaryType | FrozenDictionaryItems,
  result: DictionarySaveEvidence,
) {
  return {
    recordId,
    businessType:
      frozen.kind === "type"
        ? "system-dictionary-type"
        : "system-dictionary-items",
    interfaceOperation:
      frozen.kind === "type"
        ? "POST /api/sysdicttype/batch-save"
        : "POST /api/sysdictdata/batch-save",
    submittedAt: frozen.submittedAt,
    attemptId: frozen.attemptId,
    attemptIdMeaning: "仅用于本地关联，不是服务器幂等键",
    frozenSubmission: exportRequest(frozen.request),
    response: exportResponse(result.response),
    error: { message: result.message },
    trace: result.facts
      ? {
          phase: result.facts.phase,
          status: result.facts.status,
          correlationId: result.facts.correlationId,
        }
      : null,
    pendingQuestions: [
      "服务器是否提交本次操作仍未确认。",
      "人工终止仅释放本地草稿，不证明失败、回滚或可安全重发。",
    ],
  };
}

/** 转换仅发生在提交快照，Form/集合原始输入和基线仍由原 owner 保留。 */
function normalizeType(type: DictionaryType): DictionaryType {
  return {
    ...structuredClone(type),
    dictCode: type.dictCode?.trim(),
    dictName: type.dictName?.trim(),
    remark: type.remark?.trim() ?? null,
    groupCode: type.groupCode?.trim() ?? null,
    groupName: type.groupName?.trim() ?? null,
    sortOrder: type.sortOrder ?? 0,
  };
}

/** 对齐后端 trim 语义并保留完整 DTO；新增不接受客户端 IsSystem 或并发版本。 */
function projectSubmittedItem(
  row: DictionaryItemRow,
  created: boolean,
): DictionaryItem {
  const error = validateDictionaryItemValues(row.current);
  if (error) throw new Error(error);
  const values: DictionaryItemValues = {
    ...row.current,
    dictCode: row.current.dictCode.trim(),
    dataNameEn: row.current.dataNameEn.trim(),
    dataNameZh: row.current.dataNameZh.trim(),
    remark: row.current.remark?.trim() ?? null,
    cssClass: row.current.cssClass?.trim() ?? null,
  };
  return {
    ...structuredClone(row.source),
    ...values,
    uid: created ? "00000000-0000-0000-0000-000000000000" : row.uid,
    version: created ? 0 : row.version,
    isSystem: created ? false : row.source.isSystem === true,
  };
}

/** 修改和软删除必须使用既有基线身份；Version 不足以由本地补成 1。 */
function requireExistingIdentity(uid: string, version: number): void {
  if (!isDictionaryUid(uid) || !isDictionaryVersion(version))
    throw new Error("字典项缺少有效的服务器身份或基线版本。");
}

/** A 的完整核对规则仅适用于当前两个字典原子端点；不把其他行级 API 升级为原子语义。 */
function requireCompleteReceipt(
  request: DictionaryTypeBatchRequest | DictionaryItemsBatchRequest,
  envelope: unknown,
): SwiftExportContractsWrappersSaveItemResult[] {
  if (
    !isRecord(envelope) ||
    envelope.success !== true ||
    envelope.error != null ||
    !isRecord(envelope.data)
  )
    throw new Error("字典保存外层成功契约不完整。");
  const batch = envelope.data;
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
    throw new Error("字典保存回执遗漏或计数不一致，不能接纳。");
  const seenKeys = new Set<string>();
  const seenUids = new Set<string>();
  return batch.items.map((value: unknown) => {
    if (
      !isRecord(value) ||
      typeof value.clientKey !== "string" ||
      seenKeys.has(value.clientKey) ||
      !isDictionaryUid(value.uid) ||
      seenUids.has(value.uid.toLowerCase())
    )
      throw new Error("字典保存回执身份缺失或重复。");
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
      throw new Error("字典保存回执与冻结操作不一致。");
    if (submitted.operation === "Create") {
      const logicalUid = "data" in submitted ? submitted.data?.uid : undefined;
      if (
        value.newVersion !== 1 ||
        value.oldVersion != null ||
        (logicalUid && value.uid.toLowerCase() === logicalUid.toLowerCase())
      )
        throw new Error("字典新增回执没有可靠的服务器身份或版本。");
    } else {
      if (
        !("uid" in submitted) ||
        !("version" in submitted) ||
        submitted.uid?.toLowerCase() !== value.uid.toLowerCase()
      )
        throw new Error("字典修改或删除回执实体身份不匹配。");
      const version = submitted.version!;
      // 当前 PG UpdateAsync 先提高实体 Version，再把它写入 OldVersion；不能假定它等于请求版本。
      const expectedVersion =
        submitted.operation === "Update" ? version + 1 : version;
      if (
        value.newVersion !== expectedVersion ||
        value.oldVersion !== expectedVersion
      )
        throw new Error("字典保存回执版本与当前端点契约不匹配。");
    }
    return value as SwiftExportContractsWrappersSaveItemResult;
  });
}

/** DTO/请求只含普通 JSON 数据，深复制后冻结，防止调用方在等待回执时改动对账材料。 */
function freezeClone<T>(value: T): T {
  const clone = structuredClone(value);
  const freeze = (item: unknown): void => {
    if (typeof item !== "object" || item === null) return;
    for (const child of Object.values(item)) freeze(child);
    Object.freeze(item);
  };
  freeze(clone);
  return clone;
}

/** 仅接受普通对象事实，避免按类型声明把任意网络正文当作成功契约。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** 恢复记录显式列出可核实字段，防止未来 DTO 扩展将凭据或无关内容带入导出。 */
function exportRequest(
  request: DictionaryTypeBatchRequest | DictionaryItemsBatchRequest,
) {
  const data = (value: DictionaryType | DictionaryItem | null | undefined) => {
    if (!value) return null;
    const fields = isRecord(value) ? value : {};
    return {
      uid: value.uid,
      version: value.version,
      dictCode: value.dictCode,
      dictName: fields.dictName,
      parentId: fields.parentId,
      groupCode: fields.groupCode,
      groupName: fields.groupName,
      dataNameEn: fields.dataNameEn,
      dataNameZh: fields.dataNameZh,
      cssClass: fields.cssClass,
      isEnabled: fields.isEnabled,
      remark: value.remark,
      sortOrder: value.sortOrder,
    };
  };
  return {
    createItems: request.createItems?.map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      data: data(item.data),
    })),
    updateItems: request.updateItems?.map((item) => ({
      clientKey: item.clientKey,
      uid: item.uid,
      version: item.version,
      rowIndex: item.rowIndex,
      data: data(item.data),
    })),
    deleteItems: request.deleteItems?.map((item) => ({
      clientKey: item.clientKey,
      uid: item.uid,
      version: item.version,
      rowIndex: item.rowIndex,
      isSoftDelete: item.isSoftDelete,
    })),
  };
}

/** 响应只保留回执/结构化错误白名单，下载材料不包含请求或响应认证头。 */
function exportResponse(response: DictionaryHttpResponse | undefined) {
  if (!response) return null;
  const envelope = isRecord(response.data) ? response.data : {};
  const error = isRecord(envelope.error) ? envelope.error : {};
  const batch = isRecord(envelope.data) ? envelope.data : {};
  return {
    status: response.status,
    success: envelope.success,
    error: { code: error.code, message: error.message, field: error.field },
    data: {
      totalCount: batch.totalCount,
      successCount: batch.successCount,
      failedCount: batch.failedCount,
      allSucceeded: batch.allSucceeded,
      items: Array.isArray(batch.items)
        ? batch.items.map((item: unknown) =>
            isRecord(item)
              ? {
                  clientKey: item.clientKey,
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
