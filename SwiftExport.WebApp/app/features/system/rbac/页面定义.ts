/** 系统设置公开页面定义供应用组装消费；入口和按钮继续按真实权限键核验。 */
export const settingsPages = {
  "personal-settings": { title: "个人设置" },
  "user-management": { title: "用户管理", permission: "System.Users.View" },
  "role-management": { title: "角色管理", permission: "System.Roles.View" },
  "permission-catalog": {
    title: "权限定义",
    permission: "System.Permissions.View",
  },
} as const;

/** 编辑动作有自己的页签身份，原列表选择和其他页签不会成为提交目标。 */
export const settingsEditors = {
  "user-create": {
    title: "创建用户",
    permission: "System.Users.View",
    write: "System.Users.Manage",
  },
  "user-profile": {
    title: "用户资料",
    permission: "System.Users.View",
    write: "System.Users.Manage",
  },
  "user-roles": {
    title: "用户角色",
    permission: "System.Users.View",
    write: "System.Users.Manage",
  },
  "user-status": {
    title: "用户状态",
    permission: "System.Users.View",
    write: "System.Users.Manage",
  },
  "password-reset": {
    title: "重置用户密码",
    permission: "System.Users.View",
    write: "System.Users.Manage",
  },
  "role-create": {
    title: "创建角色",
    permission: "System.Roles.View",
    write: "System.Roles.Manage",
  },
  "role-profile": {
    title: "角色资料",
    permission: "System.Roles.View",
    write: "System.Roles.Manage",
  },
  "role-permissions": {
    title: "角色权限",
    permission: "System.Roles.View",
    write: "System.Roles.Manage",
  },
  "role-status": {
    title: "角色状态",
    permission: "System.Roles.View",
    write: "System.Roles.Manage",
  },
  "role-delete": {
    title: "删除角色",
    permission: "System.Roles.View",
    write: "System.Roles.Manage",
  },
  "password-change": {
    title: "修改我的密码",
    permission: undefined,
    write: undefined,
  },
} as const;

/** 仅上述已经实现的编辑用例可进入统一的系统设置编辑会话。 */
export type SettingsEditorKind = keyof typeof settingsEditors;

/** 新建有独立草稿身份，本人改密为单一实例，其他动作必须定位真实实体。 */
export function validSettingsTarget(
  kind: SettingsEditorKind,
  params: { id?: string; draft?: string },
): boolean {
  if (kind === "password-change") return !params.id && !params.draft;
  if (kind.endsWith("-create")) return !!params.draft && !params.id;
  return (
    !!params.id &&
    !params.draft &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      params.id,
    ) &&
    !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(params.id)
  );
}
