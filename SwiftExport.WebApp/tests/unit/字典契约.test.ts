import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createDictionaryItemRow,
  createDictionaryItemRows,
  updateDictionaryItemRows,
  removeDictionaryItemRows,
  restoreDictionaryItemRows,
  discardDictionaryItemRows,
  isProtectedDictionaryType,
  isVisibleDictionaryType,
  parseDictionarySortOrder,
} from "../../app/features/system/dictionaries/model/字典模型";
import type {
  DictionaryItem,
  DictionaryType,
} from "../../app/features/system/dictionaries/model/字典模型";
import {
  freezeDictionaryType,
  freezeDictionaryItems,
  acceptDictionaryTypeBatch,
  acceptDictionaryItemsBatch,
  createDictionaryRecoveryRecord,
} from "../../app/features/system/dictionaries/model/提交契约";
import type {
  DictionaryTypeBatchRequest,
  DictionaryItemsBatchRequest,
} from "../../app/features/system/dictionaries/model/提交契约";
import {
  saveDictionaryType,
  saveDictionaryItems,
} from "../../app/features/system/dictionaries/mutations";
import {
  fetchDictionaryTypes,
  fetchDictionaryItems,
} from "../../app/features/system/dictionaries/queries";
import { getEditableChanges } from "../../app/shared/editing/编辑集合";

const typeUid = "11111111-1111-4111-8111-111111111111";
const firstUid = "22222222-2222-4222-8222-222222222222";
const secondUid = "33333333-3333-4333-8333-333333333333";
const serverUid = "44444444-4444-4444-8444-444444444444";

/** 真实已有 DTO 包含未展示层级、分组和审计字段，提交不得把它们重置为空。 */
function typeDto(): DictionaryType {
  return {
    uid: typeUid,
    version: 4,
    dictCode: "Custom",
    dictName: "自定义",
    isSystem: false,
    parentId: firstUid,
    groupCode: "Basic",
    groupName: "基础",
    sortOrder: 2,
    remark: "备注",
    createdBy: "server-user",
  };
}

/** 构造有效既有项，隐藏样式字段保留在可写投影和原服务器快照中。 */
function itemDto(uid = firstUid): DictionaryItem {
  return {
    uid,
    version: 3,
    dictCode: "Custom",
    dataNameEn: "Before",
    dataNameZh: "原值",
    cssClass: "kept-css",
    remark: "保留备注",
    sortOrder: null,
    isEnabled: false,
    isSystem: false,
    createdBy: "server-user",
  };
}

/** HTTP 样例直接采用当前后端字符串枚举和仓储回执规则，不用模拟保存器的协议。 */
function successBody(
  request: DictionaryTypeBatchRequest | DictionaryItemsBatchRequest,
) {
  const items = [
    ...(request.createItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Create",
      uid: serverUid,
      success: true,
      oldVersion: null,
      newVersion: 1,
      errorCode: null,
      message: "新增成功",
    })),
    ...(request.updateItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Update",
      uid: item.uid,
      success: true,
      oldVersion: item.version! + 1,
      newVersion: item.version! + 1,
      errorCode: null,
      message: "更新成功",
    })),
    ...(request.deleteItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Delete",
      uid: item.uid,
      success: true,
      oldVersion: item.version!,
      newVersion: item.version!,
      errorCode: null,
      message: "删除成功",
    })),
  ];
  return {
    success: true,
    message: "保存成功",
    error: null,
    data: {
      totalCount: items.length,
      successCount: items.length,
      failedCount: 0,
      allSucceeded: true,
      addSuccessCount: request.createItems?.length ?? 0,
      updateSuccessCount: request.updateItems?.length ?? 0,
      deleteSuccessCount: request.deleteItems?.length ?? 0,
      items,
    },
  };
}

/** 精确替代 HTTP 传输，仍经过真实生成操作和共享 mutator，不接触运行库。 */
function respond(body: unknown, status = 200) {
  return vi.stubGlobal(
    "fetch",
    vi.fn(
      async () =>
        new Response(JSON.stringify(body), {
          status,
          headers: {
            "Content-Type": "application/json",
            "X-Correlation-ID": "dictionary-unit-trace",
          },
        }),
    ),
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("字典冻结与编辑集合", () => {
  it("类型修改保留隐藏层级分组字段且冻结副本不受Form后续变化影响", () => {
    const baseline = typeDto();
    const current = { ...baseline, dictName: " 修改 " };
    const frozen = freezeDictionaryType({
      operation: "update",
      clientKey: "type-client",
      current,
      baseline,
    });
    current.dictName = "后续变化";
    expect(frozen.request.updateItems?.[0]).toMatchObject({
      uid: typeUid,
      version: 4,
      data: {
        dictName: "修改",
        parentId: firstUid,
        groupCode: "Basic",
        groupName: "基础",
        createdBy: "server-user",
      },
    });
    expect(baseline.dictName).toBe("自定义");
    expect(Object.isFrozen(frozen.request.updateItems?.[0]?.data)).toBe(true);
  });

  it("新类型只保留逻辑身份，完整成功用服务器Uid和Version建立基线", () => {
    const frozen = freezeDictionaryType({
      operation: "create",
      clientKey: "new-type",
      current: { dictCode: "New", dictName: "新字典", sortOrder: 0 },
    });
    expect(frozen.current.uid).toMatch(/^[0-9a-f-]{36}$/);
    expect(frozen.current.version).toBe(0);
    const accepted = acceptDictionaryTypeBatch(
      frozen,
      successBody(frozen.request),
    );
    expect(accepted).toMatchObject({
      uid: serverUid,
      version: 1,
      dictCode: "New",
      dictName: "新字典",
    });
    expect(accepted?.uid).not.toBe(frozen.current.uid);
  });

  it("字典项使用互斥增改删并保留未展示样式与禁用状态", () => {
    let rows = createDictionaryItemRows([itemDto(), itemDto(secondUid)]);
    rows = updateDictionaryItemRows(rows, firstUid, { dataNameEn: "After" });
    rows = removeDictionaryItemRows(rows, secondUid);
    rows.push(createDictionaryItemRow("Custom", "new-row"));
    rows = updateDictionaryItemRows(rows, "new-row", {
      dataNameEn: "Created",
      dataNameZh: "新增",
    });
    const frozen = freezeDictionaryItems(rows);
    expect(frozen.request.updateItems?.[0]?.data).toMatchObject({
      cssClass: "kept-css",
      isEnabled: false,
      createdBy: "server-user",
      uid: firstUid,
      version: 3,
    });
    expect(frozen.request.deleteItems).toHaveLength(1);
    const accepted = acceptDictionaryItemsBatch(
      frozen,
      successBody(frozen.request),
    );
    expect(accepted.map((row) => row.uid)).toEqual([firstUid, serverUid]);
    expect(accepted[0]?.version).toBe(4);
    expect(getEditableChanges(accepted)).toEqual({
      created: [],
      updated: [],
      deleted: [],
    });
    expect(accepted[0]?.source.createdBy).toBe("server-user");
  });

  it("删除恢复保留草稿，放弃恢复基线，不丢只读来源", () => {
    let rows = createDictionaryItemRows([itemDto()]);
    rows = updateDictionaryItemRows(rows, firstUid, { dataNameZh: "编辑值" });
    rows = removeDictionaryItemRows(rows, firstUid);
    rows = restoreDictionaryItemRows(rows, firstUid);
    expect(rows[0]?.current.dataNameZh).toBe("编辑值");
    rows = discardDictionaryItemRows(rows);
    expect(rows[0]?.current.dataNameZh).toBe("原值");
    expect(rows[0]?.source.cssClass).toBe("kept-css");
  });

  it("七个系统编码不依赖旧IsSystem标记，旧品质字典不会回到维护入口", () => {
    for (const dictCode of [
      "CustomerType",
      "Incoterm",
      "PaymentMethod",
      "ShippedBy",
      "Unit",
      "FlowTestFunction",
      "FlowLimiterSpecification",
    ])
      expect(
        isProtectedDictionaryType({
          dictCode: dictCode.toLowerCase(),
          isSystem: false,
        }),
      ).toBe(true);
    expect(
      isProtectedDictionaryType({ dictCode: "Custom", isSystem: false }),
    ).toBe(false);
    expect(
      isVisibleDictionaryType({ dictCode: "AfterSalesQualityCategory " }),
    ).toBe(false);
    expect(() =>
      freezeDictionaryType({
        operation: "delete",
        clientKey: "protected",
        current: { ...typeDto(), dictCode: "Unit" },
        baseline: { ...typeDto(), dictCode: "Unit" },
      }),
    ).toThrow("不能删除");
  });

  it("无效/未完成排序不强制变零，类型非空排序和可空项排序区分", () => {
    expect(parseDictionarySortOrder(" ")).toEqual({ valid: true, value: null });
    expect(parseDictionarySortOrder(" ", false)).toEqual({ valid: false });
    expect(parseDictionarySortOrder("+")).toEqual({ valid: false });
    expect(parseDictionarySortOrder("2147483648")).toEqual({ valid: false });
    expect(parseDictionarySortOrder("-12")).toEqual({
      valid: true,
      value: -12,
    });
    expect(() =>
      freezeDictionaryItems([createDictionaryItemRow("Custom")]),
    ).toThrow("英文名称");
  });
});

describe("完整字典回执", () => {
  it.each([
    "missing",
    "duplicate",
    "wrong-key",
    "wrong-operation",
    "wrong-version",
    "partial",
    "count",
    "numeric-enum",
  ])("%s回执不能部分接纳或解锁", (scenario) => {
    const baseline = typeDto();
    const frozen = freezeDictionaryType({
      operation: "update",
      clientKey: "type-client",
      current: { ...baseline, dictName: "修改" },
      baseline,
    });
    const body = successBody(frozen.request);
    if (scenario === "missing") body.data.items = [];
    if (scenario === "duplicate")
      body.data.items.push({ ...body.data.items[0]! });
    if (scenario === "wrong-key")
      body.data.items[0]!.clientKey = "other-client";
    if (scenario === "wrong-operation")
      body.data.items[0]!.operation = "Delete";
    if (scenario === "wrong-version") body.data.items[0]!.newVersion = 999;
    if (scenario === "partial") body.data.items[0]!.success = false;
    if (scenario === "count") body.data.totalCount = 2;
    if (scenario === "numeric-enum")
      Object.assign(body.data.items[0]!, { operation: 2 });
    expect(() => acceptDictionaryTypeBatch(frozen, body)).toThrow();
    expect(baseline.dictName).toBe("自定义");
    expect(baseline.version).toBe(4);
  });

  it("当前更新OldVersion为加一值，软删除版本保持；不擅自要求旧版本等于请求", () => {
    const baseline = typeDto();
    const update = freezeDictionaryType({
      operation: "update",
      clientKey: "update",
      current: baseline,
      baseline,
    });
    expect(
      acceptDictionaryTypeBatch(update, successBody(update.request))?.version,
    ).toBe(5);
    const deletion = freezeDictionaryType({
      operation: "delete",
      clientKey: "delete",
      current: baseline,
      baseline,
    });
    expect(
      acceptDictionaryTypeBatch(deletion, successBody(deletion.request)),
    ).toBeNull();
  });
});

describe("真实生成链上的提交分类", () => {
  it("完整成功才返回A，同时发送普通JSON并保留追踪事实", async () => {
    const baseline = typeDto();
    const frozen = freezeDictionaryType({
      operation: "update",
      clientKey: "type-client",
      current: baseline,
      baseline,
    });
    respond(successBody(frozen.request));
    const result = await saveDictionaryType(frozen);
    expect(result.kind).toBe("confirmedSuccess");
    expect(result.facts).toMatchObject({
      phase: "response",
      status: 200,
      correlationId: "dictionary-unit-trace",
    });
    const options = vi.mocked(fetch).mock.calls[0]?.[1];
    expect(new Headers(options?.headers).get("Content-Type")).toBe(
      "application/json",
    );
    expect(JSON.parse(String(options?.body)).updateItems[0].clientKey).toBe(
      "type-client",
    );
  });

  it.each([
    [400, "VALIDATION"],
    [403, "FORBIDDEN"],
    [429, "RATE_LIMITED"],
  ])("精确前置短路%s/%s为B", async (status, code) => {
    const frozen = freezeDictionaryType({
      operation: "create",
      clientKey: "new",
      current: { dictCode: "New", dictName: "新类型" },
    });
    respond(
      { success: false, data: null, error: { code, message: "前置拒绝" } },
      status as number,
    );
    expect((await saveDictionaryType(frozen)).kind).toBe("confirmedNoEffect");
  });

  it.each([
    [422, "BUSINESS_BLOCKED"],
    [500, "UNEXPECTED"],
    [503, "OUTCOME_UNKNOWN"],
    [400, "BUSINESS_BLOCKED"],
    [403, "UNEXPECTED"],
  ])("无法确认%s/%s仍为C，即使显示已回滚", async (status, code) => {
    const frozen = freezeDictionaryType({
      operation: "create",
      clientKey: "new",
      current: { dictCode: "New", dictName: "新类型" },
    });
    respond(
      {
        success: false,
        data: null,
        error: { code, message: "所有操作已回滚，请重新提交" },
      },
      status as number,
    );
    const result = await saveDictionaryType(frozen);
    expect(result.kind).toBe("unknown");
    expect(result.message).toContain("已回滚");
    expect(result.facts?.status).toBe(status);
  });

  it("确定未发的取消为B，派发后的网络丢失为C且不重发", async () => {
    const frozen = freezeDictionaryType({
      operation: "create",
      clientKey: "new",
      current: { dictCode: "New", dictName: "新类型" },
    });
    const request = vi.fn(async () => {
      throw new TypeError("lost-response");
    });
    vi.stubGlobal("fetch", request);
    const controller = new AbortController();
    controller.abort();
    const notSent = await saveDictionaryType(frozen, {
      signal: controller.signal,
    });
    expect(notSent.kind).toBe("confirmedNoEffect");
    expect(request).not.toHaveBeenCalled();
    const lost = await saveDictionaryType(frozen);
    expect(lost.kind).toBe("unknown");
    expect(lost.facts?.phase).toBe("dispatched");
    expect(request).toHaveBeenCalledTimes(1);
  });

  it("200残缺或不可解析响应均C，冻结项不会被acceptChanges", async () => {
    const rows = updateDictionaryItemRows(
      createDictionaryItemRows([itemDto()]),
      firstUid,
      { dataNameEn: "After" },
    );
    const frozen = freezeDictionaryItems(rows);
    const body = successBody(frozen.request);
    body.data.items = [];
    respond(body);
    expect((await saveDictionaryItems(frozen)).kind).toBe("unknown");
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response("broken-json", { status: 200 })),
    );
    const invalid = await saveDictionaryItems(frozen);
    expect(invalid.kind).toBe("unknown");
    expect(invalid.facts?.rawBody).toBe("broken-json");
    expect(getEditableChanges(rows).updated).toHaveLength(1);
  });

  it("恢复记录白名单保全响应与请求，不导出Authorization、token或完整响应头", async () => {
    const frozen = freezeDictionaryType({
      operation: "create",
      clientKey: "new",
      current: { dictCode: "New", dictName: "新类型" },
    });
    respond(
      {
        success: false,
        data: null,
        access_token: "never-export-token",
        error: { code: "UNEXPECTED", message: "待核实" },
      },
      500,
    );
    const result = await saveDictionaryType(frozen);
    const record = createDictionaryRecoveryRecord("record-1", frozen, result);
    const json = JSON.stringify(record);
    expect(json).toContain("record-1");
    expect(json).toContain(frozen.attemptId);
    expect(json).toContain("dictionary-unit-trace");
    expect(json).not.toContain("never-export-token");
    expect(json).not.toContain("Authorization");
    expect(record.response?.error.code).toBe("UNEXPECTED");
  });
});

describe("字典读取快照", () => {
  it("类型读取保留真实DTO并排除旧品质入口，项读取保留禁用项", async () => {
    respond({
      success: true,
      error: null,
      data: [
        typeDto(),
        { ...typeDto(), uid: secondUid, dictCode: "AfterSalesQualityCategory" },
      ],
    });
    const types = await fetchDictionaryTypes();
    expect(types).toHaveLength(1);
    expect(types[0]?.parentId).toBe(firstUid);
    respond({ success: true, error: null, data: [itemDto()] });
    const items = await fetchDictionaryItems("Custom");
    expect(items[0]?.isEnabled).toBe(false);
    expect(items[0]?.cssClass).toBe("kept-css");
  });

  it("错误code或残缺基线不可替换现有会话", async () => {
    respond({
      success: true,
      error: null,
      data: [{ ...itemDto(), dictCode: "Other" }],
    });
    await expect(fetchDictionaryItems("Custom")).rejects.toThrow("当前类型");
    respond({
      success: true,
      error: null,
      data: [{ ...typeDto(), version: 0 }],
    });
    await expect(fetchDictionaryTypes()).rejects.toThrow("完整唯一身份");
  });
});
