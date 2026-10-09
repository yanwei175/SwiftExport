import { afterEach, describe, expect, it, vi } from "vitest";
import { queryOptions } from "@tanstack/react-query";
import { createQueryClient } from "./查询客户端";
import { queryKeys } from "./查询键";
import { queryPolicies } from "./查询策略";

afterEach(() => vi.useRealTimers());

describe("原生 Query 缓存策略", () => {
  it.each([
    ["live", 0],
    ["lookup", 30_000],
    ["reference", 300_000],
  ] as const)(
    "%s 策略按新鲜期复用，过期才重新读取",
    async (kind, staleTime) => {
      vi.useFakeTimers();
      const client = createQueryClient();
      const read = vi.fn().mockResolvedValue({ server: true });
      const options = queryOptions({
        queryKey: ["profile", kind],
        queryFn: read,
        ...queryPolicies[kind],
      });
      try {
        await client.fetchQuery(options);
        await client.fetchQuery(options);
        expect(read).toHaveBeenCalledTimes(staleTime === 0 ? 2 : 1);
        if (staleTime) {
          vi.advanceTimersByTime(staleTime);
          await client.fetchQuery(options);
          expect(read).toHaveBeenCalledTimes(2);
        }
      } finally {
        client.clear();
      }
    },
  );

  it.each([
    ["live", 300_000],
    ["lookup", 600_000],
    ["reference", 1_800_000],
  ] as const)("%s 仅在无观察者时按约定回收快照", async (kind, gcTime) => {
    vi.useFakeTimers();
    const client = createQueryClient();
    const key = ["gc", kind];
    await client.fetchQuery(
      queryOptions({
        queryKey: key,
        queryFn: async () => "snapshot",
        ...queryPolicies[kind],
      }),
    );
    vi.advanceTimersByTime(gcTime - 1);
    expect(client.getQueryData(key)).toBe("snapshot");
    vi.advanceTimersByTime(1);
    expect(client.getQueryData(key)).toBeUndefined();
    client.clear();
  });

  it("读取失败不自动重试，默认策略不开聚焦刷新或轮询", async () => {
    const client = createQueryClient();
    const read = vi.fn().mockRejectedValue(new Error("模拟读取失败"));
    await expect(
      client.fetchQuery({ queryKey: ["failed"], queryFn: read }),
    ).rejects.toThrow("模拟读取失败");
    expect(read).toHaveBeenCalledTimes(1);
    expect(client.getDefaultOptions().queries).toMatchObject({
      refetchOnWindowFocus: false,
      refetchInterval: false,
    });
    expect(client.getDefaultOptions().mutations?.retry).toBe(false);
    client.clear();
  });

  it("用户、身份编号、权限编号和输入分别隔离缓存", () => {
    const client = createQueryClient();
    const identity = { userId: "user-a", identityEpoch: 1, permissionEpoch: 0 };
    const key = queryKeys.operation(identity, "products.page", { page: 1 });
    client.setQueryData(key, "original");
    expect(
      client.getQueryData(
        queryKeys.operation({ ...identity }, "products.page", { page: 1 }),
      ),
    ).toBe("original");
    for (const other of [
      { ...identity, userId: "user-b" },
      { ...identity, identityEpoch: 2 },
      { ...identity, permissionEpoch: 1 },
    ])
      expect(
        client.getQueryData(
          queryKeys.operation(other, "products.page", { page: 1 }),
        ),
      ).toBeUndefined();
    expect(
      client.getQueryData(
        queryKeys.operation(identity, "products.page", { page: 2 }),
      ),
    ).toBeUndefined();
    expect(queryKeys.me(identity)).toEqual(
      queryKeys.operation(identity, "auth.me"),
    );
    client.clear();
  });
});
