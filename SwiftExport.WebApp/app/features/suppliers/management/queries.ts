import type { QueryClient } from "@tanstack/react-query";
import { getSupplierPage } from "../../../shared/api/generated/client";
import type { SwiftExportContractsDTOsSuppliersRequestsSupplierSearchRequest } from "../../../shared/api/generated/client";
import { ApiResponseError } from "../../../shared/api/响应错误";
import type { QueryIdentity } from "../../../shared/query/查询键";
import { isSupplierUid, isSupplierVersion } from "./model/供应商模型";
import type { SupplierDto } from "./model/供应商模型";

/** 查询参数仅引用真实生成类型，批量关键词未在当前后端查询中生效。 */
export type SupplierSearchRequest =
  SwiftExportContractsDTOsSuppliersRequestsSupplierSearchRequest;

/** POST page 是读取；检查成功与分页身份后返回快照，不自动覆盖业务草稿。 */
export async function fetchSupplierPage(
  request: SupplierSearchRequest,
  signal?: AbortSignal,
): Promise<{ items: SupplierDto[]; totalCount: number }> {
  const response = await getSupplierPage(request, {
    signal,
    headers: { "Content-Type": "application/json" },
  });
  const body: unknown = response.data;
  if (
    response.status !== 200 ||
    !isRecord(body) ||
    body.success !== true ||
    body.error != null ||
    !isRecord(body.data) ||
    !Array.isArray(body.data.items) ||
    !Number.isInteger(body.data.totalCount) ||
    (body.data.totalCount as number) < body.data.items.length
  )
    throw new ApiResponseError(
      response,
      "供应商分页响应不完整，不能作为编辑基线。",
    );
  const seen = new Set<string>();
  const items: SupplierDto[] = [];
  for (const value of body.data.items) {
    if (
      !isRecord(value) ||
      !isSupplierUid(value.uid) ||
      !isSupplierVersion(value.version) ||
      value.isDeleted === true ||
      seen.has(value.uid.toLowerCase())
    )
      throw new ApiResponseError(
        response,
        "供应商响应缺少唯一身份或有效基线版本。",
      );
    seen.add(value.uid.toLowerCase());
    items.push(value as SupplierDto);
  }
  return { items, totalCount: body.data.totalCount as number };
}

/** 成功只失效当前身份与核权范围内的供应商分页，缓存读取不接纳草稿。 */
export async function invalidateSupplierQueries(
  queryClient: QueryClient,
  identity: QueryIdentity,
): Promise<void> {
  await queryClient.invalidateQueries({
    predicate: (query) =>
      query.queryKey[0] === "identity" &&
      query.queryKey[1] === identity.identityEpoch &&
      query.queryKey[2] === identity.userId &&
      query.queryKey[3] === identity.permissionEpoch &&
      query.queryKey[4] === "getSupplierPage",
  });
}

/** 网络正文先按运行时对象核对，不把生成器可选字段等同于成功证据。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
