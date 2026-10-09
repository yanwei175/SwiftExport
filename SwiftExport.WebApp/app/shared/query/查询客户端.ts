import { QueryClient } from "@tanstack/react-query";
import { queryPolicies } from "./查询策略";

/** 创建唯一查询客户端；读写均不自动重试，也不以窗口聚焦刷新覆盖编辑基线。 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: queryPolicies.live,
      mutations: { retry: false },
    },
  });
}
