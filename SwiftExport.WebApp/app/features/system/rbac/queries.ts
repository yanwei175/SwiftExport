import { useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getManagedUsersPage,
  getManagedRolesPage,
  getManagedUser,
  getManagedRole,
  getUserRoles,
  getRolePermissions,
  getUserEffectivePermissions,
  getManagedPermissions,
} from "../../../shared/api/generated/client";
import type {
  SwiftExportContractsDTOsAuthManagedUserDto as ManagedUser,
  SwiftExportContractsDTOsAuthManagedRoleDto as ManagedRole,
  SwiftExportContractsDTOsAuthManagedPermissionDto as ManagedPermission,
  SwiftExportContractsDTOsAuthUserRolesDto as UserRoles,
  SwiftExportContractsDTOsAuthRolePermissionsDto as RolePermissions,
  SwiftExportContractsDTOsAuthManagedPageRequest as PageRequest,
} from "../../../shared/api/generated/client";
import { ApiResponseError } from "../../../shared/api/响应错误";
import { hasPermission } from "../../../shared/authorization/权限检查";
import { useSession } from "../../../shared/session/会话上下文";
import { queryKeys } from "../../../shared/query/查询键";
import { queryPolicies } from "../../../shared/query/查询策略";
import type { SettingsEditorKind } from "./页面定义";
export type { ManagedUser, ManagedRole, ManagedPermission };

/** 查询必须完整成功；该业务的 ServiceResult 解释留在 feature 内。 */
function readResult<T>(response: {
  status: number;
  data: { success?: boolean; data?: T | null; error?: unknown };
  headers: Headers;
}): T {
  if (
    response.status !== 200 ||
    response.data.success !== true ||
    response.data.error != null ||
    response.data.data == null
  )
    throw new ApiResponseError(response, "系统设置读取失败，请重试。");
  return response.data.data;
}
/** 真实聚合身份和版本是编辑基线的最低要求。 */
export function validEntity(value: {
  uid?: string;
  version?: number;
}): boolean {
  return (
    typeof value.uid === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
      value.uid,
    ) &&
    value.uid !== "00000000-0000-0000-0000-000000000000" &&
    Number.isInteger(value.version) &&
    value.version! > 0
  );
}
/** 完整用户快照必须带有效名称和真实状态，不能把部分正文接纳为资料基线。 */
export function validManagedUser(value: unknown): value is ManagedUser {
  if (typeof value !== "object" || value === null) return false;
  const row = value as ManagedUser;
  return (
    validEntity(row) &&
    typeof row.userName === "string" &&
    !!row.userName.trim() &&
    typeof row.nickName === "string" &&
    !!row.nickName.trim() &&
    (row.email == null || typeof row.email === "string") &&
    (row.phoneNumber == null || typeof row.phoneNumber === "string") &&
    typeof row.isActive === "boolean" &&
    typeof row.isAdmin === "boolean"
  );
}
/** 内置标记和稳定编码属于角色只读身份，成功响应不能遗漏。 */
export function validManagedRole(value: unknown): value is ManagedRole {
  if (typeof value !== "object" || value === null) return false;
  const row = value as ManagedRole;
  return (
    validEntity(row) &&
    typeof row.roleCode === "string" &&
    !!row.roleCode.trim() &&
    typeof row.roleName === "string" &&
    !!row.roleName.trim() &&
    (row.description == null || typeof row.description === "string") &&
    typeof row.isActive === "boolean" &&
    typeof row.isSystem === "boolean"
  );
}
/** 后端分页不是全量；候选和管理列表都不能把空缺页当作删除。 */
export async function fetchUsers(request: PageRequest, signal?: AbortSignal) {
  const response = await getManagedUsersPage(request, {
    signal,
    headers: { "Content-Type": "application/json" },
  });
  const page = readResult(response);
  if (
    !Array.isArray(page.items) ||
    page.items.some((row) => !validManagedUser(row)) ||
    new Set(page.items.map((row) => row.uid?.toLowerCase())).size !==
      page.items.length ||
    !Number.isInteger(page.totalCount) ||
    page.totalCount! < page.items.length ||
    page.pageIndex !== request.pageIndex ||
    page.pageSize !== request.pageSize
  )
    throw new ApiResponseError(response, "用户分页响应不完整。");
  return { items: page.items, totalCount: page.totalCount! };
}
/** 角色读取保留停用及系统角色，页面操作再核对不可变边界。 */
export async function fetchRoles(request: PageRequest, signal?: AbortSignal) {
  const response = await getManagedRolesPage(request, {
    signal,
    headers: { "Content-Type": "application/json" },
  });
  const page = readResult(response);
  if (
    !Array.isArray(page.items) ||
    page.items.some((row) => !validManagedRole(row)) ||
    new Set(page.items.map((row) => row.uid?.toLowerCase())).size !==
      page.items.length ||
    !Number.isInteger(page.totalCount) ||
    page.totalCount! < page.items.length ||
    page.pageIndex !== request.pageIndex ||
    page.pageSize !== request.pageSize
  )
    throw new ApiResponseError(response, "角色分页响应不完整。");
  return { items: page.items, totalCount: page.totalCount! };
}
/** 权限目录是后端只读定义，不从当前用户权限伪造管理目录。 */
export async function fetchPermissions(
  signal?: AbortSignal,
): Promise<ManagedPermission[]> {
  const response = await getManagedPermissions({ signal });
  const items = readResult(response);
  if (
    !Array.isArray(items) ||
    items.some(
      (row) =>
        !row.uid || !row.permissionKey || !row.groupKey || !row.displayName,
    ) ||
    new Set(items.map((row) => row.uid)).size !== items.length
  )
    throw new ApiResponseError(response, "权限目录响应不完整或身份重复。");
  return items;
}
/** 表单初始化材料只保留服务器基线和候选，当前值由 Ant Form 独占。 */
export interface SettingsEditorSource {
  user?: ManagedUser;
  role?: ManagedRole;
  userRoles?: UserRoles;
  rolePermissions?: RolePermissions;
  roles?: ManagedRole[];
  permissions?: ManagedPermission[];
  effective?: string[];
}
/** 角色候选遍历真实页数，缺页/重复身份会失败，不能以不完整目录整组替换。 */
async function allRoles(signal?: AbortSignal): Promise<ManagedRole[]> {
  const rows: ManagedRole[] = [];
  for (let pageIndex = 1; ; pageIndex++) {
    const page = await fetchRoles({ pageIndex, pageSize: 200 }, signal);
    rows.push(...page.items);
    if (
      new Set(rows.map((row) => row.uid)).size !== rows.length ||
      rows.length > page.totalCount ||
      (page.items.length === 0 && rows.length < page.totalCount)
    )
      throw new Error("角色候选分页不完整，请重试读取。");
    if (rows.length === page.totalCount) return rows;
  }
}
/** 每个 tabId 从自身目标读取；缺候选权限时不调用受限端点，也不能编辑对应分配。 */
export async function fetchEditorSource(
  kind: SettingsEditorKind,
  id: string,
  canReadRoles: boolean,
  canReadPermissions: boolean,
  signal?: AbortSignal,
): Promise<SettingsEditorSource> {
  if (kind.endsWith("-create") || kind === "password-change") return {};
  if (kind.startsWith("user-") || kind === "password-reset") {
    const response = await getManagedUser(id, { signal });
    const user = readResult(response);
    if (!validManagedUser(user) || user.uid?.toLowerCase() !== id.toLowerCase())
      throw new ApiResponseError(response, "用户详情身份或版本不一致。");
    if (kind !== "user-roles") return { user };
    const relation = readResult(await getUserRoles(id, { signal }));
    if (
      relation.userId?.toLowerCase() !== id.toLowerCase() ||
      !Number.isInteger(relation.version) ||
      relation.version! < 1 ||
      !Array.isArray(relation.roleIds)
    )
      throw new Error("用户角色版本或集合不完整。");
    const roles = canReadRoles ? await allRoles(signal) : undefined;
    const effective = readResult(
      await getUserEffectivePermissions(id, { signal }),
    );
    if (
      !Array.isArray(effective) ||
      effective.some((key) => typeof key !== "string")
    )
      throw new Error("有效权限响应不完整。");
    return { user, userRoles: relation, roles, effective };
  }
  const response = await getManagedRole(id, { signal });
  const role = readResult(response);
  if (!validManagedRole(role) || role.uid?.toLowerCase() !== id.toLowerCase())
    throw new ApiResponseError(response, "角色详情身份或版本不一致。");
  if (kind !== "role-permissions") return { role };
  const relation = readResult(await getRolePermissions(id, { signal }));
  if (
    relation.roleId?.toLowerCase() !== id.toLowerCase() ||
    !Number.isInteger(relation.version) ||
    relation.version! < 1 ||
    !Array.isArray(relation.permissionIds) ||
    typeof relation.isAutomatic !== "boolean"
  )
    throw new Error("角色权限版本或集合不完整。");
  return {
    role,
    rolePermissions: relation,
    permissions: canReadPermissions
      ? await fetchPermissions(signal)
      : undefined,
  };
}
/** 活动状态显式控制外置 Query，权限 epoch 隔离撤权后的旧快照。 */
export function useSettingsQuery<T>(
  operation: string,
  input: unknown,
  isActive: boolean,
  permission: string | undefined,
  queryFn: (signal: AbortSignal) => Promise<T>,
) {
  const session = useSession();
  const queryClient = useQueryClient();
  const enabled =
    isActive &&
    session.status === "authenticated" &&
    (!permission || hasPermission(session.user, permission));
  const key = queryKeys.operation(
    {
      userId: session.user?.userId ?? "",
      identityEpoch: session.identityEpoch,
      permissionEpoch: session.permissionEpoch,
    },
    operation,
    input,
  );
  const signature = JSON.stringify(key);
  // Activity 不接管外部读取，失活时只取消这个身份/操作的 Query，不取消写命令。
  useEffect(() => {
    if (!enabled)
      void queryClient.cancelQueries({
        predicate: (query) => JSON.stringify(query.queryKey) === signature,
      });
  }, [enabled, queryClient, signature]);
  return useQuery({
    ...queryPolicies.live,
    queryKey: key,
    enabled,
    queryFn: ({ signal }) => queryFn(signal),
  });
}
