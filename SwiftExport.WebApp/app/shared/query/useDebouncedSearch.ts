import { useEffect, useMemo, useState } from "react";

/** 搜索词保持本轮输入；只有 isReady 为真时，feature 才能启用使用该词的远程 Query。 */
export interface DebouncedSearch {
  readonly value: string;
  readonly isReady: boolean;
}

/** 输入和活动授权形成一次等待身份；重新激活或新输入不能复用上一次就绪标记。 */
interface SearchRequest {
  readonly value: string;
  readonly enabled: boolean;
}

/** 固定 300ms 的可取消调度辅助；不执行请求、不保存候选或业务当前值，供 hook 及定向验证复用。 */
export function scheduleDebouncedSearch(
  value: string,
  enabled: boolean,
  onReady: (value: string) => void,
): () => void {
  if (!enabled) return () => undefined;
  let active = true;
  const timer = setTimeout(() => {
    if (active) onReady(value);
  }, 300);
  return () => {
    active = false;
    clearTimeout(timer);
  };
}

/**
 * 为 feature 的远程搜索提供固定 300ms 准备门槛；enabled 应为 isActive && authorized。
 * 新输入、隐藏、撤权及卸载取消旧 timer；本轮请求身份在 render 即失效旧就绪标记。
 * value 始终为本轮词，防抖期间 isReady=false；调用方仍负责词规范化、Query Key、权限和响应接纳。
 * SSR 不运行 Effect，不读取 window 或浏览器存储，也不宣称候选端点具备服务端搜索能力。
 */
export function useDebouncedSearch(
  value: string,
  enabled: boolean,
): DebouncedSearch {
  const request = useMemo<SearchRequest>(
    () => ({ value, enabled }),
    [value, enabled],
  );
  const [readyRequest, setReadyRequest] = useState<SearchRequest | null>(null);
  useEffect(
    () =>
      scheduleDebouncedSearch(request.value, request.enabled, () =>
        setReadyRequest(request),
      ),
    [request],
  );
  return { value, isReady: enabled && readyRequest === request };
}
