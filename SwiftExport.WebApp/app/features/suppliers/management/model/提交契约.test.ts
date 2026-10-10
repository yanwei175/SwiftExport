import { describe, expect, it } from "vitest";
import { createSupplierRow, createSupplierRows } from "./供应商模型";
import {
  acceptSupplierBatch,
  createSupplierRecoveryRecord,
  freezeSupplierRows,
} from "./提交契约";
import type { FrozenSuppliers } from "./提交契约";

const ids = [
  "11111111-1111-1111-1111-111111111111",
  "22222222-2222-2222-2222-222222222222",
  "33333333-3333-3333-3333-333333333333",
];
const createdUid = "44444444-4444-4444-4444-444444444444";

/** 以当前 PG 真实版本规则构造模拟回执，仅验证前端对账，不作为运行库验收。 */
function receipt(frozen: FrozenSuppliers) {
  const request = frozen.request;
  const items = [
    ...(request.createItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Create",
      uid: createdUid,
      oldVersion: null,
      newVersion: 1,
      success: true,
      errorCode: null,
    })),
    ...(request.updateItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Update",
      uid: item.uid,
      oldVersion: item.version! + 1,
      newVersion: item.version! + 1,
      success: true,
      errorCode: null,
    })),
    ...(request.deleteItems ?? []).map((item) => ({
      clientKey: item.clientKey,
      rowIndex: item.rowIndex,
      operation: "Delete",
      uid: item.uid,
      oldVersion: item.version,
      newVersion: item.version,
      success: true,
      errorCode: null,
    })),
  ];
  return {
    success: true,
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

/** 混合新增、修改、删除及未修改行检验一次整批接纳的边界。 */
function mixedRows() {
  const rows = createSupplierRows(
    ids.map((uid, index) => ({
      uid,
      version: index + 3,
      supplierCode: `S${index}`,
      supplierName: `名称${index}`,
      docRelateNo: `D${index}`,
      currency: "CNY",
    })),
  );
  rows[0]!.current.supplierName = " 修改名称 ";
  rows[1] = { ...rows[1]!, deleted: true };
  const added = createSupplierRow("new-client-key");
  added.current = {
    ...added.current,
    supplierCode: " S3 ",
    supplierName: "新增名称",
    docRelateNo: " D3 ",
  };
  return [...rows, added];
}

describe("供应商原子提交及恢复材料", () => {
  it("冻结独立增改删和原版本，完整回执后才更新基线及真实身份", () => {
    const rows = mixedRows();
    const frozen = freezeSupplierRows(rows);
    rows[0]!.current.supplierName = "后改值";
    expect(frozen.request.createItems?.[0]?.data).toMatchObject({
      supplierCode: "S3",
      docRelateNo: "D3",
      uid: "00000000-0000-0000-0000-000000000000",
      version: 0,
    });
    expect(frozen.request.updateItems?.[0]).toMatchObject({
      uid: ids[0],
      version: 3,
      data: { supplierName: "修改名称" },
    });
    expect(frozen.request.deleteItems?.[0]).toMatchObject({
      uid: ids[1],
      version: 4,
      isSoftDelete: true,
    });
    expect(Object.isFrozen(frozen.request.updateItems?.[0]?.data)).toBe(true);
    expect(Object.isFrozen(rows[0]!.current)).toBe(false);
    const accepted = acceptSupplierBatch(frozen, receipt(frozen));
    expect(accepted).toHaveLength(3);
    expect(accepted[0]).toMatchObject({
      uid: ids[0],
      version: 4,
      baseline: { supplierName: "修改名称" },
      current: { supplierName: "修改名称" },
      source: { supplierName: "修改名称", version: 4 },
    });
    expect(
      accepted.find((row) => row.clientKey === "new-client-key"),
    ).toMatchObject({ uid: createdUid, version: 1, origin: "existing" });
    expect(accepted.some((row) => row.uid === ids[1])).toBe(false);
    expect(accepted.find((row) => row.uid === ids[2])?.version).toBe(5);
  });

  it("遗漏、重复、部分失败、错操作、错身份或错版本全部拒绝整批接纳", () => {
    const frozen = freezeSupplierRows(mixedRows());
    const base = receipt(frozen);
    const invalid = [
      { ...base, data: { ...base.data, items: base.data.items.slice(1) } },
      { ...base, data: { ...base.data, successCount: 0 } },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 0
              ? { ...item, clientKey: base.data.items[1]!.clientKey }
              : item,
          ),
        },
      },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 0 ? { ...item, success: false } : item,
          ),
        },
      },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 1 ? { ...item, operation: "Delete" } : item,
          ),
        },
      },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 1 ? { ...item, uid: createdUid } : item,
          ),
        },
      },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 1 ? { ...item, oldVersion: 3 } : item,
          ),
        },
      },
      {
        ...base,
        data: {
          ...base.data,
          items: base.data.items.map((item, index) =>
            index === 0 ? { ...item, uid: ids[2] } : item,
          ),
        },
      },
    ];
    for (const envelope of invalid)
      expect(() => acceptSupplierBatch(frozen, envelope)).toThrow();
    expect(frozen.rows[0]!.current.supplierName).toBe(" 修改名称 ");
  });

  it("重复代码、空必填及无修改均在冻结前阻止请求", () => {
    expect(() =>
      freezeSupplierRows(createSupplierRows([{ uid: ids[0], version: 1 }])),
    ).toThrow("没有需要保存");
    expect(() => freezeSupplierRows([createSupplierRow()])).toThrow(
      "代码不能为空",
    );
    const rows = mixedRows();
    rows.at(-1)!.current.supplierCode = "s0";
    expect(() => freezeSupplierRows(rows)).toThrow("重复供应商代码");
  });

  it("恢复导出保留冻结请求及追踪白名单，不泄露原始正文、认证头或额外字段", () => {
    const frozen = freezeSupplierRows(mixedRows());
    const record = createSupplierRecoveryRecord("record-1", frozen, {
      message: "仍需核实",
      response: {
        status: 500,
        headers: new Headers({ Authorization: "secret-header" }),
        data: {
          success: false,
          authorization: "secret-body",
          error: {
            code: "UNEXPECTED",
            message: "失败",
            accessToken: "secret-error",
          },
        },
      },
      facts: {
        path: "/api/supplier/batch-save",
        method: "POST",
        phase: "response",
        credentialRevision: 1,
        status: 500,
        rawBody: "secret-raw",
        correlationId: "supplier-trace",
      },
    });
    expect(record).toMatchObject({
      interfaceOperation: "POST /api/supplier/batch-save",
      attemptId: frozen.attemptId,
      trace: { correlationId: "supplier-trace" },
    });
    expect(record.frozenSubmission.createItems?.[0]?.data?.supplierCode).toBe(
      "S3",
    );
    expect(record.frozenSubmission).toEqual(frozen.request);
    expect(JSON.stringify(record)).not.toMatch(
      /secret-|CommandId|Authorization|rawBody/,
    );
  });
});
