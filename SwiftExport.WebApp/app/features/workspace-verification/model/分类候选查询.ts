import { useQuery } from "@tanstack/react-query";
import { getProductCategories } from "../../../shared/api/generated/client";
import { ApiResponseError } from "../../../shared/api/响应错误";
import { queryKeys } from "../../../shared/query/查询键";
import type { QueryIdentity } from "../../../shared/query/查询键";
import { queryPolicies } from "../../../shared/query/查询策略";
import type { LookupOption } from "../../../shared/ui/候选选择";

/** 消费已有 all 端点及独立分类读取权；搜索由 feature 本地过滤，不虚构服务端搜索接口。 */
export function useCategoryOptions(identity: QueryIdentity, enabled: boolean) {
  return useQuery({
    ...queryPolicies.lookup,
    queryKey: queryKeys.operation(identity, "productCategory.all"),
    enabled,
    queryFn: async ({ signal }): Promise<LookupOption<string>[]> => {
      const response = await getProductCategories({ signal });
      if (
        response.status !== 200 ||
        response.data.success !== true ||
        !Array.isArray(response.data.data)
      )
        throw new ApiResponseError(response, "分类候选未能读取。");
      return response.data.data.flatMap((item) =>
        typeof item.uid === "string" && typeof item.categoryName === "string"
          ? [{ value: item.uid, label: item.categoryName }]
          : [],
      );
    },
  });
}
