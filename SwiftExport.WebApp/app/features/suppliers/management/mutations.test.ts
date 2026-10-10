import { afterEach, describe, expect, it, vi } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { setAccessToken } from "../../../shared/api/请求执行";
import { queryKeys } from "../../../shared/query/查询键";
import { createSupplierRow } from "./model/供应商模型";
import { freezeSupplierRows } from "./model/提交契约";
import { saveSuppliers } from "./mutations";
import { fetchSupplierPage, invalidateSupplierQueries } from "./queries";

const uid = "11111111-1111-1111-1111-111111111111";

/** 模拟 Fetch 正文只用于前端集成契约，不连接真实服务器或数据库。 */
function http(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      "X-Correlation-ID": "supplier-test",
    },
  });
}

/** 本地新增输入须满足实际必填边界，单据关联号不以临时 ClientKey 替代。 */
function frozen() {
  const row = createSupplierRow("local-key");
  row.current = {
    ...row.current,
    supplierCode: "S1",
    supplierName: "供应商",
    docRelateNo: "D1",
  };
  return freezeSupplierRows([row]);
}

afterEach(() => {
  setAccessToken(null);
  vi.restoreAllMocks();
});

describe("供应商生成请求与结果分类", () => {
  it("完整真实回执返回 A，普通 DTO 发送 JSON、Bearer 和 omit，不自动重试", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      http({
        success: true,
        error: null,
        data: {
          totalCount: 1,
          successCount: 1,
          failedCount: 0,
          allSucceeded: true,
          addSuccessCount: 1,
          updateSuccessCount: 0,
          deleteSuccessCount: 0,
          items: [
            {
              clientKey: "local-key",
              rowIndex: 1,
              operation: "Create",
              uid,
              success: true,
              oldVersion: null,
              newVersion: 1,
              errorCode: null,
            },
          ],
        },
      }),
    );
    setAccessToken("test-token");
    const result = await saveSuppliers(frozen());
    expect(result.kind).toBe("confirmedSuccess");
    expect(result.message).toBe("供应商整批保存回执已完整核验。");
    expect(fetch).toHaveBeenCalledTimes(1);
    const [path, options] = fetch.mock.calls[0]!;
    expect(path).toBe("/api/supplier/batch-save");
    expect(options?.credentials).toBe("omit");
    expect(new Headers(options?.headers).get("Content-Type")).toBe(
      "application/json",
    );
    expect(new Headers(options?.headers).get("Authorization")).toBe(
      "Bearer test-token",
    );
    expect(
      JSON.parse(String(options?.body)).createItems[0].data.docRelateNo,
    ).toBe("D1");
  });

  it("发送前已经取消才返回 B，派发网络失败仍为 C", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockRejectedValue(new Error("网络断开"));
    const controller = new AbortController();
    controller.abort();
    const unsent = await saveSuppliers(frozen(), { signal: controller.signal });
    expect(unsent.kind).toBe("confirmedNoEffect");
    expect(unsent.facts?.phase).toBe("not-sent");
    expect(fetch).not.toHaveBeenCalled();
    const dispatched = await saveSuppliers(frozen());
    expect(dispatched.kind).toBe("unknown");
    expect(dispatched.facts?.phase).toBe("dispatched");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it.each([
    [400, "VALIDATION"],
    [401, "UNAUTHENTICATED"],
    [403, "FORBIDDEN"],
    [422, "BUSINESS_BLOCKED"],
    [429, "RATE_LIMITED"],
    [500, "UNEXPECTED"],
    [503, "UNAVAILABLE"],
  ])("HTTP %s 缺少供应商独立短路证据保持 C", async (status, code) => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      http(
        { success: false, data: null, error: { code, message: "服务器说明" } },
        status as number,
      ),
    );
    const result = await saveSuppliers(frozen());
    expect(result.kind).toBe("unknown");
    expect(result.message).toBe("服务器说明");
    expect(result.response?.status).toBe(status);
    expect(result.facts?.phase).toBe("response");
  });

  it("200 缺失回执和无法解析正文保持 C 并保留传输事实", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        http({ success: true, data: { allSucceeded: true, items: [] } }),
      )
      .mockResolvedValueOnce(new Response("broken-json", { status: 200 }));
    const incomplete = await saveSuppliers(frozen());
    expect(incomplete.kind).toBe("unknown");
    expect(incomplete.response?.status).toBe(200);
    const invalid = await saveSuppliers(frozen());
    expect(invalid.kind).toBe("unknown");
    expect(invalid.facts?.phase).toBe("response");
  });
});

describe("供应商读取与精确失效", () => {
  it("生成 POST page 透传 signal，失败不伪造空集合，重复身份拒绝成为基线", async () => {
    const dto = {
      uid,
      version: 1,
      supplierCode: "S1",
      supplierName: "供应商",
      docRelateNo: "D1",
    };
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        http({
          success: true,
          data: { items: [dto], totalCount: 3 },
          error: null,
        }),
      )
      .mockResolvedValueOnce(
        http(
          {
            success: false,
            data: null,
            error: { code: "FORBIDDEN", message: "无权" },
          },
          403,
        ),
      )
      .mockResolvedValueOnce(
        http({ success: true, data: { items: [dto, dto], totalCount: 2 } }),
      );
    const controller = new AbortController();
    expect(
      await fetchSupplierPage(
        { keyword: "S", pageIndex: 1, pageSize: 20 },
        controller.signal,
      ),
    ).toEqual({ items: [dto], totalCount: 3 });
    expect(fetch.mock.calls[0]?.[0]).toBe("/api/supplier/page");
    expect(fetch.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
    await expect(fetchSupplierPage({})).rejects.toThrow("供应商分页响应");
    await expect(fetchSupplierPage({})).rejects.toThrow("唯一身份");
  });

  it("保存只失效同一用户、身份和核权版本的 getSupplierPage", async () => {
    const queryClient = new QueryClient();
    const identity = { userId: "user-a", identityEpoch: 2, permissionEpoch: 3 };
    const own = queryKeys.operation(identity, "getSupplierPage", {
      pageIndex: 1,
    });
    const otherOperation = queryKeys.operation(identity, "getDictionaryTypes");
    const otherPermission = queryKeys.operation(
      { ...identity, permissionEpoch: 4 },
      "getSupplierPage",
    );
    const otherUser = queryKeys.operation(
      { ...identity, userId: "user-b" },
      "getSupplierPage",
    );
    for (const key of [own, otherOperation, otherPermission, otherUser])
      queryClient.setQueryData(key, []);
    await invalidateSupplierQueries(queryClient, identity);
    expect(queryClient.getQueryState(own)?.isInvalidated).toBe(true);
    for (const key of [otherOperation, otherPermission, otherUser])
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(false);
    queryClient.clear();
  });
});
