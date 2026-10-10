import { lazy } from "react";
import type { PermissionKey } from "../shared/authorization/generated/权限键";
import type { ComponentType, LazyExoticComponent } from "react";
import type {
  OpenIntent,
  TabDescriptor,
  WorkspaceTabProps,
} from "../shared/workspace/页签契约";
import {
  settingsPages,
  settingsEditors,
  validSettingsTarget,
} from "../features/system/rbac/页面定义";
import type { SettingsEditorKind } from "../features/system/rbac/页面定义";

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
const settingsEntry = lazy(
  () => import("../features/system/rbac/设置页签入口"),
);
const settingsEditorEntry = lazy(
  () => import("../features/system/rbac/编辑页签入口"),
);
/** 唯一页面目录也供菜单和地址解析消费。 */
export const pageDirectory: Readonly<Record<string, PageDefinition>> = {
  ...Object.fromEntries(
    Object.entries(settingsPages).map(([kind, definition]) => [
      kind,
      { ...definition, component: settingsEntry },
    ]),
  ),
  ...Object.fromEntries(
    Object.entries(settingsEditors).map(([kind, definition]) => [
      kind,
      {
        title: definition.title,
        permission: definition.permission,
        component: settingsEditorEntry,
      },
    ]),
  ),
  home: {
    title: "首页",
    component: lazy(() => import("../features/home/首页页签入口")),
  },
  "dictionary-management": {
    title: "字典管理",
    permission: "System.Dictionary.View",
    component: lazy(
      () => import("../features/system/dictionaries/字典页签入口"),
    ),
  },
  "supplier-management": {
    title: "供应商管理",
    permission: "Suppliers.Supplier.View" satisfies PermissionKey,
    component: lazy(
      () => import("../features/suppliers/management/供应商页签入口"),
    ),
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
    (intent.pageKind !== "supplier-management" ||
      (!intent.params?.id && !intent.params?.draft)) &&
    (!(intent.pageKind in settingsPages) ||
      (!intent.params?.id && !intent.params?.draft)) &&
    (!(intent.pageKind in settingsEditors) ||
      validSettingsTarget(
        intent.pageKind as SettingsEditorKind,
        intent.params ?? {},
      )) &&
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
  const params =
    intent.pageKind in settingsEditors && intent.params?.id
      ? { id: intent.params.id.toLowerCase() }
      : (intent.params ?? {});
  return {
    tabId: crypto.randomUUID(),
    pageKind: intent.pageKind,
    params,
    title: definition.title,
    identity: `${intent.pageKind}:${params.id ?? params.draft ?? "list"}`,
  };
}
