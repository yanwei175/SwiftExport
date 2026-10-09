import type { UseQueryOptions } from "@tanstack/react-query";

/** 只描述服务器快照的新鲜度；业务草稿、权限及生命周期由各自 owner 管理。 */
export type QueryFreshness = "live" | "lookup" | "reference";

const sharedOptions = {
  retry: false,
  refetchOnWindowFocus: false,
  refetchOnMount: true,
  refetchOnReconnect: true,
  refetchInterval: false,
} as const;

/** 直接展开到原生 queryOptions/useQuery；gcTime 只回收无观察者快照，不驱逐页签。 */
export const queryPolicies = {
  live: { ...sharedOptions, staleTime: 0, gcTime: 5 * 60 * 1000 },
  lookup: { ...sharedOptions, staleTime: 30 * 1000, gcTime: 10 * 60 * 1000 },
  reference: {
    ...sharedOptions,
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  },
} as const satisfies Record<
  QueryFreshness,
  Pick<
    UseQueryOptions,
    | "retry"
    | "refetchOnWindowFocus"
    | "refetchOnMount"
    | "refetchOnReconnect"
    | "refetchInterval"
    | "staleTime"
    | "gcTime"
  >
>;
