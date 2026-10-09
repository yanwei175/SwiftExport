import { lazy } from "react";
import type { ComponentType, LazyExoticComponent } from "react";
import type {
  OpenIntent,
  TabDescriptor,
  WorkspaceTabProps,
} from "../shared/workspace/页签契约";

/** 页面注册统一定位、权限与显式拆包，不登记尚未实现的业务页。 */
export interface PageDefinition {
  title: string;
  permission?: string;
  component: LazyExoticComponent<ComponentType<WorkspaceTabProps>>;
}

/** 验收页只在明确启用的构建中开放，不构成真实业务写入能力。 */
export const probesEnabled =
  import.meta.env.VITE_ENABLE_WORKSPACE_PROBES === "true";
const probeEntry = probesEnabled
  ? lazy(() => import("../features/workspace-verification/验证页签入口"))
  : null;
/** 唯一页面目录也供菜单和地址解析消费。 */
export const pageDirectory: Readonly<Record<string, PageDefinition>> = {
  home: {
    title: "首页",
    component: lazy(() => import("../features/home/首页页签入口")),
  },
  ...(probeEntry
    ? {
        "verification-form": {
          title: "表单验收",
          permission: "Products.Product.View",
          component: probeEntry,
        },
        "verification-grid": {
          title: "列表验收",
          permission: "Products.Product.View",
          component: probeEntry,
        },
      }
    : {}),
};

/** 参数只包含业务定位字段；保存草稿与凭据禁止进入 URL。 */
export function validIntent(intent: OpenIntent): boolean {
  return (
    Boolean(pageDirectory[intent.pageKind]) &&
    [intent.params?.id, intent.params?.draft].every(
      (value) => value === undefined || /^[a-zA-Z0-9-]{1,80}$/.test(value),
    ) &&
    !(intent.params?.id && intent.params?.draft)
  );
}

/** 相同实体定位原实例，新草稿拥有独立身份，tabId 不随业务保存改变。 */
export function createDescriptor(intent: OpenIntent): TabDescriptor {
  const definition = pageDirectory[intent.pageKind];
  if (!definition || !validIntent(intent)) throw new Error("页面参数无效。");
  const params = intent.params ?? {};
  return {
    tabId: crypto.randomUUID(),
    pageKind: intent.pageKind,
    params,
    title: definition.title,
    identity: `${intent.pageKind}:${params.id ?? params.draft ?? "list"}`,
  };
}
