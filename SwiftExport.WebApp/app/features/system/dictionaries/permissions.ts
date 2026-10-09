import type { SwiftExportContractsDTOsAuthMeDto as MeDto } from "../../../shared/api/generated/client";
import { hasPermission } from "../../../shared/authorization/权限检查";
import { useSession } from "../../../shared/session/会话上下文";

/** 字典操作保持后端既有的四个权限键，不以角色名或管理员标记扩权。 */
export interface DictionaryPermissions {
  readonly view: boolean;
  readonly create: boolean;
  readonly edit: boolean;
  readonly delete: boolean;
}

/** 页面、提交与恢复导出共同消费这四个后端已存在的精确权限键。 */
export const dictionaryPermissions = {
  view: "System.Dictionary.View",
  create: "System.Dictionary.Create",
  edit: "System.Dictionary.Edit",
  delete: "System.Dictionary.Delete",
} as const;

/** 认证有效时精确消费权限投影；保存入口仍须再次读取最新状态。 */
export function getDictionaryPermissions(
  user: MeDto | null,
  authenticated: boolean,
): DictionaryPermissions {
  return {
    view: authenticated && hasPermission(user, dictionaryPermissions.view),
    create: authenticated && hasPermission(user, dictionaryPermissions.create),
    edit: authenticated && hasPermission(user, dictionaryPermissions.edit),
    delete: authenticated && hasPermission(user, dictionaryPermissions.delete),
  };
}

/** 已打开页面随当前身份和核权结果更新，重认证时不消费旧权限。 */
export function useDictionaryPermissions(): DictionaryPermissions {
  const session = useSession();
  return getDictionaryPermissions(
    session.user,
    session.status === "authenticated",
  );
}
