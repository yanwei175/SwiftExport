import type { OpenIntent, TabDescriptor } from "../shared/workspace/页签契约";
import { validIntent } from "./页面目录";

/** 地址只用于活动页定位，隐藏页不得以它重新获取身份。 */
export function parseTabAddress(search: string): OpenIntent | null {
  const params = new URLSearchParams(search);
  const pageKind = params.get("view");
  if (!pageKind) return null;
  const intent: OpenIntent = {
    pageKind,
    params: {
      ...(params.has("id") ? { id: params.get("id")! } : {}),
      ...(params.has("draft") ? { draft: params.get("draft")! } : {}),
    },
  };
  return validIntent(intent) ? intent : null;
}
/** 写入的定位信息来自该页描述，不包含完整 DTO 或原始输入。 */
export function tabAddress(tab: TabDescriptor): string {
  const params = new URLSearchParams({ view: tab.pageKind });
  if (tab.params.id) params.set("id", tab.params.id);
  if (tab.params.draft) params.set("draft", tab.params.draft);
  return `?${params}`;
}
