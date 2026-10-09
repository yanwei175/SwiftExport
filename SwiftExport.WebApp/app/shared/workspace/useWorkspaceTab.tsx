import { createContext, useContext } from "react";
import type { TabHost } from "./页签契约";

/** 由应用页签容器注入，仅在该稳定实例内部可访问。 */
export const TabHostContext = createContext<TabHost | null>(null);

/** 获取本页宿主契约；缺少宿主时立即报错以避免另造标签管理器。 */
export function useWorkspaceTab(): TabHost {
  const host = useContext(TabHostContext);
  if (!host) throw new Error("页签必须在工作区宿主中运行。");
  return host;
}
