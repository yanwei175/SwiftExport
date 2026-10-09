import type { SwiftExportContractsDTOsAuthMeDto as MeDto } from "../api/generated/client";
import { useSession } from "../session/会话上下文";
import { permissionKeys } from "./generated/权限键";

const knownPermissions = new Set<string>(permissionKeys);

/** 精确消费 me 的已知权限投影；不以角色显示名称或 isAdmin 推测任意权限。 */
export function hasPermission(user: MeDto | null, key: string): boolean {
  return Boolean(knownPermissions.has(key) && user?.permissions?.includes(key));
}

/** 页面与操作统一核验当前会话，重认证期间不放行旧权限快照。 */
export function usePermission(key: string): boolean {
  const { status, user } = useSession();
  return status === "authenticated" && hasPermission(user, key);
}
