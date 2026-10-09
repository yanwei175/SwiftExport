import { useQuery } from "@tanstack/react-query";
import type { QueryClient } from "@tanstack/react-query";
import {
  getDictionaryTypes,
  getDictionaryByCode,
} from "../../../shared/api/generated/client";
import { ApiResponseError } from "../../../shared/api/响应错误";
import { queryKeys } from "../../../shared/query/查询键";
import type { QueryIdentity } from "../../../shared/query/查询键";
import { queryPolicies } from "../../../shared/query/查询策略";
import { useSession } from "../../../shared/session/会话上下文";
import { useDictionaryPermissions } from "./permissions";
import {
  isDictionaryUid,
  isDictionaryVersion,
  isVisibleDictionaryType,
} from "./model/字典模型";
import type { DictionaryType, DictionaryItem } from "./model/字典模型";

/** 只通过生成读取操作取得原 DTO；空/失败响应不靠中文消息猜测为合法空列表。 */
export async function fetchDictionaryTypes(
  signal?: AbortSignal,
): Promise<DictionaryType[]> {
  const response = await getDictionaryTypes({ signal });
  if (
    response.status !== 200 ||
    response.data.success !== true ||
    !Array.isArray(response.data.data)
  )
    throw new ApiResponseError(response, "字典类型未能读取。");
  const seen = new Set<string>();
  for (const item of response.data.data) {
    if (
      !isDictionaryUid(item.uid) ||
      !isDictionaryVersion(item.version) ||
      !item.dictCode?.trim() ||
      !item.dictName?.trim() ||
      seen.has(item.uid.toLowerCase())
    )
      throw new ApiResponseError(
        response,
        "字典类型响应缺少完整唯一身份、版本或必填字段。",
      );
    seen.add(item.uid.toLowerCase());
  }
  return response.data.data.filter(isVisibleDictionaryType);
}

/** by-code 返回全部字典项；启用标记和未展示 DTO 字段不被候选显示逻辑过滤掉。 */
export async function fetchDictionaryItems(
  dictCode: string,
  signal?: AbortSignal,
): Promise<DictionaryItem[]> {
  const code = dictCode.trim();
  if (!code) throw new Error("读取字典项需要明确的字典编码。");
  const response = await getDictionaryByCode(code, { signal });
  if (
    response.status !== 200 ||
    response.data.success !== true ||
    !Array.isArray(response.data.data)
  )
    throw new ApiResponseError(response, "字典项未能读取。");
  const seen = new Set<string>();
  for (const item of response.data.data) {
    if (
      !isDictionaryUid(item.uid) ||
      !isDictionaryVersion(item.version) ||
      item.dictCode?.trim().toLowerCase() !== code.toLowerCase() ||
      !item.dataNameEn?.trim() ||
      !item.dataNameZh?.trim() ||
      seen.has(item.uid.toLowerCase())
    )
      throw new ApiResponseError(
        response,
        "字典项响应与当前类型或完整基线不一致。",
      );
    seen.add(item.uid.toLowerCase());
  }
  return response.data.data;
}

/** Activity 外读取显式按活动状态及权限启用；Query 快照从不回写 Form 基线。 */
export function useDictionaryTypesQuery(isActive: boolean) {
  const session = useSession();
  const permissions = useDictionaryPermissions();
  const identity = {
    userId: session.user?.userId ?? "",
    identityEpoch: session.identityEpoch,
    permissionEpoch: session.permissionEpoch,
  };
  return useQuery({
    ...queryPolicies.live,
    queryKey: queryKeys.operation(identity, "getDictionaryTypes"),
    enabled: isActive && permissions.view,
    queryFn: ({ signal }) => fetchDictionaryTypes(signal),
  });
}

/** 隐藏页暂停显示读取，当前类型和身份/核权版本共同隔离候选缓存。 */
export function useDictionaryItemsQuery(
  dictCode: string | null,
  isActive: boolean,
) {
  const session = useSession();
  const permissions = useDictionaryPermissions();
  const identity = {
    userId: session.user?.userId ?? "",
    identityEpoch: session.identityEpoch,
    permissionEpoch: session.permissionEpoch,
  };
  return useQuery({
    ...queryPolicies.live,
    queryKey: queryKeys.operation(identity, "getDictionaryByCode", dictCode),
    enabled: isActive && permissions.view && !!dictCode?.trim(),
    queryFn: ({ signal }) => fetchDictionaryItems(dictCode ?? "", signal),
  });
}

/** 完整成功只标记当前身份相关字典读取失效，不把缓存刷新直接接纳为草稿或基线。 */
export async function invalidateDictionaryQueries(
  queryClient: QueryClient,
  identity: QueryIdentity,
): Promise<void> {
  await queryClient.invalidateQueries({
    predicate: (query) =>
      query.queryKey[0] === "identity" &&
      query.queryKey[1] === identity.identityEpoch &&
      query.queryKey[2] === identity.userId &&
      query.queryKey[3] === identity.permissionEpoch &&
      ["getDictionaryTypes", "getDictionaryByCode"].includes(
        String(query.queryKey[4]),
      ),
  });
}
