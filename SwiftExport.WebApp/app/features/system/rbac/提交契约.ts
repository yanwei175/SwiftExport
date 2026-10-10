import {
  registerUser,
  changeOwnPassword,
  resetUserPassword,
  updateManagedUser,
  setManagedUserStatus,
  replaceUserRoles,
  createManagedRole,
  updateManagedRole,
  setManagedRoleStatus,
  deleteManagedRole,
  replaceRolePermissions,
} from "../../../shared/api/generated/client";
import {
  RequestTransportError,
  getRequestFacts,
} from "../../../shared/api/请求执行";
import type { RequestFacts } from "../../../shared/api/请求执行";
import { ApiResponseError } from "../../../shared/api/响应错误";
import { validManagedRole, validManagedUser } from "./queries";
import type { SettingsEditorKind } from "./页面定义";
import type { SettingsEditorSource } from "./queries";

/** 所有当前值由 Ant Form 持有；这里的类型只描述控件值，不能另建可写 DTO 副本。 */
export interface SettingsFormValues {
  userName?: string;
  nickName?: string;
  email?: string;
  phoneNumber?: string;
  roleCode?: string;
  roleName?: string;
  description?: string;
  ids?: string[];
  isActive?: boolean;
  confirmed?: boolean;
  currentPassword?: string;
  password?: string;
  confirmPassword?: string;
}
/** 原始冻结值只在当前内存会话用于本次提交；密码绝不进入恢复导出或诊断。 */
export interface FrozenSettingsSubmission {
  readonly kind: SettingsEditorKind;
  readonly targetId: string;
  readonly version: number;
  readonly attemptId: string;
  readonly submittedAt: string;
  readonly values: Readonly<SettingsFormValues>;
}
/** 生成器没有声明账号成功正文；保留 unknown，由真实用例逐项核对。 */
export interface SettingsHttpResponse {
  status: number;
  data: unknown;
  headers: Headers;
}
/** A 接纳、B 保留可修改输入、C 保留冻结和追踪，不以 HTTP 状态猜事务结果。 */
export type SettingsSaveEvidence =
  | { outcome: "confirmed"; data: unknown; response: SettingsHttpResponse }
  | {
      outcome: "not-applied" | "unknown";
      error: string;
      field?: string;
      facts?: RequestFacts;
      response?: SettingsHttpResponse;
    };

/** 只有参与保存的字段归一化，关系集合按身份比较，筛选和错误不参与 dirty。 */
export function projectSettingsValues(
  kind: SettingsEditorKind,
  values: SettingsFormValues,
): SettingsFormValues {
  if (kind === "user-create")
    return {
      userName: values.userName?.trim() ?? "",
      nickName: values.nickName?.trim() ?? "",
      email: values.email?.trim() ?? "",
      phoneNumber: values.phoneNumber?.trim() ?? "",
      password: values.password ?? "",
      confirmPassword: values.confirmPassword ?? "",
    };
  if (kind === "user-profile")
    return {
      nickName: values.nickName?.trim() ?? "",
      email: values.email?.trim() ?? "",
      phoneNumber: values.phoneNumber?.trim() ?? "",
    };
  if (kind === "role-create")
    return {
      roleCode: values.roleCode?.trim() ?? "",
      roleName: values.roleName?.trim() ?? "",
      description: values.description?.trim() ?? "",
    };
  if (kind === "role-profile")
    return {
      roleName: values.roleName?.trim() ?? "",
      description: values.description?.trim() ?? "",
    };
  if (kind === "user-roles" || kind === "role-permissions")
    return {
      ids: [...(values.ids ?? [])].map((id) => id.toLowerCase()).sort(),
    };
  if (kind.endsWith("-status")) return { isActive: values.isActive === true };
  if (kind === "role-delete") return { confirmed: values.confirmed === true };
  return {
    ...(kind === "password-change"
      ? { currentPassword: values.currentPassword ?? "" }
      : {}),
    password: values.password ?? "",
    confirmPassword: values.confirmPassword ?? "",
  };
}
/** 服务端基线只在初始化、放弃或已确认接纳时投影为表单值。 */
export function initialSettingsValues(
  kind: SettingsEditorKind,
  source: SettingsEditorSource,
): SettingsFormValues {
  return projectSettingsValues(kind, {
    userName: source.user?.userName ?? "",
    nickName: source.user?.nickName ?? "",
    email: source.user?.email ?? "",
    phoneNumber: source.user?.phoneNumber ?? "",
    roleCode: source.role?.roleCode ?? "",
    roleName: source.role?.roleName ?? "",
    description: source.role?.description ?? "",
    isActive: source.user?.isActive ?? source.role?.isActive,
    ids:
      source.userRoles?.roleIds ?? source.rolePermissions?.permissionIds ?? [],
    confirmed: false,
  });
}
/** 整组替换包含当前完整选值，不能因搜索隐藏而截断，也不能提交已失效候选。 */
export function validateSettingsValues(
  kind: SettingsEditorKind,
  values: SettingsFormValues,
  source: SettingsEditorSource,
): string | null {
  const current = projectSettingsValues(kind, values);
  if (kind === "user-roles") {
    if (!source.user?.isActive || !source.roles)
      return "目标用户已停用或没有完整角色候选读取权限。";
    if (
      current.ids?.some(
        (id) =>
          !source.roles?.some(
            (role) => role.uid?.toLowerCase() === id && role.isActive,
          ),
      )
    )
      return "请选择有效角色；已停用或缺失角色需要明确移除后再保存。";
  }
  if (kind === "role-permissions") {
    if (
      source.role?.isSystem ||
      source.rolePermissions?.isAutomatic ||
      !source.permissions
    )
      return "内置角色自动授权或缺少完整权限目录读取权限。";
    if (
      current.ids?.some(
        (id) =>
          !source.permissions?.some(
            (permission) =>
              permission.uid?.toLowerCase() === id &&
              permission.isActive &&
              permission.isAssignable,
          ),
      )
    )
      return "请选择有效且可分配的权限；失效或保留权限需要明确移除后再保存。";
  }
  if (
    kind === "password-reset" &&
    (!source.user?.isActive || source.user.isAdmin)
  )
    return "只能重置活动普通用户密码；管理员密码由本人修改或现有恢复工具处理。";
  if (
    kind.startsWith("role-") &&
    kind !== "role-create" &&
    source.role?.isSystem
  )
    return "内置角色不可修改、启停、删除或保存权限关系。";
  if (kind === "role-delete" && !current.confirmed)
    return "请先确认删除该角色。";
  return null;
}
/** 冻结独立提交快照，页面后续操作不能改变请求；attemptId 只用于本地关联。 */
export function freezeSettingsSubmission(
  kind: SettingsEditorKind,
  id: string,
  version: number,
  values: SettingsFormValues,
): FrozenSettingsSubmission {
  const snapshot = structuredClone(projectSettingsValues(kind, values));
  if (snapshot.ids) Object.freeze(snapshot.ids);
  return Object.freeze({
    kind,
    targetId: id,
    version,
    attemptId: crypto.randomUUID(),
    submittedAt: new Date().toISOString(),
    values: Object.freeze(snapshot),
  });
}
/** 写请求唯一入口只调用 Orval 生成操作；不取消已发请求、不自动重试。 */
export async function submitSettings(
  frozen: FrozenSettingsSubmission,
): Promise<SettingsSaveEvidence> {
  const v = frozen.values;
  const expectedVersion = frozen.version;
  const id = frozen.targetId;
  const options = { headers: { "Content-Type": "application/json" } };
  let response: SettingsHttpResponse;
  try {
    switch (frozen.kind) {
      case "user-create":
        response = await registerUser(
          {
            userName: v.userName,
            nickName: v.nickName,
            email: v.email,
            phoneNumber: v.phoneNumber,
            password: v.password,
            confirmPassword: v.confirmPassword,
          },
          options,
        );
        break;
      case "user-profile":
        response = await updateManagedUser(
          id,
          {
            expectedVersion,
            nickName: v.nickName,
            email: v.email,
            phoneNumber: v.phoneNumber,
          },
          options,
        );
        break;
      case "user-status":
        response = await setManagedUserStatus(
          id,
          { expectedVersion, isActive: v.isActive },
          options,
        );
        break;
      case "user-roles":
        response = await replaceUserRoles(
          id,
          { expectedVersion, roleIds: [...(v.ids ?? [])] },
          options,
        );
        break;
      case "role-create":
        response = await createManagedRole(
          {
            roleCode: v.roleCode,
            roleName: v.roleName,
            description: v.description,
          },
          options,
        );
        break;
      case "role-profile":
        response = await updateManagedRole(
          id,
          { expectedVersion, roleName: v.roleName, description: v.description },
          options,
        );
        break;
      case "role-status":
        response = await setManagedRoleStatus(
          id,
          { expectedVersion, isActive: v.isActive },
          options,
        );
        break;
      case "role-delete":
        response = await deleteManagedRole(id, { expectedVersion });
        break;
      case "role-permissions":
        response = await replaceRolePermissions(
          id,
          { expectedVersion, permissionIds: [...(v.ids ?? [])] },
          options,
        );
        break;
      case "password-change":
        response = await changeOwnPassword(
          {
            currentPassword: v.currentPassword,
            newPassword: v.password,
            confirmPassword: v.confirmPassword,
          },
          options,
        );
        break;
      case "password-reset":
        response = await resetUserPassword(
          {
            userId: id,
            newPassword: v.password,
            confirmPassword: v.confirmPassword,
          },
          options,
        );
        break;
    }
  } catch (error) {
    return {
      outcome:
        error instanceof RequestTransportError &&
        error.facts.phase === "not-sent"
          ? "not-applied"
          : "unknown",
      error: error instanceof Error ? error.message : "提交结果仍需核实。",
      facts: error instanceof RequestTransportError ? error.facts : undefined,
    };
  }
  return classifySettingsResponse(frozen, response);
}
/** 失败文字只供展示。RBAC 回滚异常被吞掉，任何已发失败都不能靠 code/422 推定 B。 */
export function classifySettingsResponse(
  frozen: FrozenSettingsSubmission,
  response: SettingsHttpResponse,
): SettingsSaveEvidence {
  const envelope = record(response.data);
  const error = record(envelope?.error);
  // 仅本人改密的这个稳定字段分支在写入前短路；已有 AuthServiceTests 定向证明零次写入。
  // 不扩大到其他 BUSINESS_BLOCKED，也不套用到 RBAC 的吞回滚异常路径。
  if (
    frozen.kind === "password-change" &&
    response.status === 422 &&
    envelope?.success === false &&
    error?.code === "BUSINESS_BLOCKED" &&
    error.field === "CurrentPassword"
  )
    return {
      outcome: "not-applied",
      error:
        typeof error.message === "string"
          ? error.message
          : "旧密码验证失败，本次未生效。",
      field: "CurrentPassword",
      response,
      facts: getRequestFacts(response),
    };
  const unknown = (text: string): SettingsSaveEvidence => ({
    outcome: "unknown",
    error: text,
    field: typeof error?.field === "string" ? error.field : undefined,
    response,
    facts: getRequestFacts(response),
  });
  if (
    response.status !== 200 ||
    envelope?.success !== true ||
    envelope.error != null
  )
    return unknown(
      typeof error?.message === "string"
        ? error.message
        : "保存没有完整成功回执，服务器结果仍需核实。",
    );
  const data = envelope.data;
  const row = record(data);
  const { kind, targetId, version, values: values } = frozen;
  const entity =
    row &&
    (kind.startsWith("user-")
      ? validManagedUser(row)
      : validManagedRole(row)) &&
    (kind.endsWith("-create")
      ? row.version === 1
      : row.uid === targetId && row.version === version + 1);
  let valid: boolean;
  switch (kind) {
    case "user-create":
      valid =
        typeof data === "string" &&
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
          data,
        ) &&
        data !== "00000000-0000-0000-0000-000000000000";
      break;
    case "user-profile":
      valid =
        !!entity &&
        row?.nickName === values.nickName &&
        (row?.email ?? "") === values.email &&
        (row?.phoneNumber ?? "") === values.phoneNumber;
      break;
    case "user-status":
      valid = !!entity && row?.isActive === values.isActive;
      break;
    case "role-create":
      valid =
        !!entity &&
        row?.roleCode === values.roleCode &&
        row?.roleName === values.roleName &&
        (row?.description ?? "") === values.description &&
        row?.isSystem === false;
      break;
    case "role-profile":
      valid =
        !!entity &&
        row?.roleName === values.roleName &&
        (row?.description ?? "") === values.description &&
        row?.isSystem === false;
      break;
    case "role-status":
      valid =
        !!entity &&
        row?.isActive === values.isActive &&
        row?.isSystem === false;
      break;
    case "user-roles":
      valid =
        row?.userId === targetId &&
        row?.version === version + 1 &&
        sameIds(row?.roleIds, values.ids);
      break;
    case "role-permissions":
      valid =
        row?.roleId === targetId &&
        row?.version === version + 1 &&
        row?.isAutomatic === false &&
        sameIds(row?.permissionIds, values.ids);
      break;
    default:
      valid = data === true;
  }
  return valid
    ? { outcome: "confirmed", data, response }
    : unknown("成功回执与冻结身份、版本或完整字段不一致，不能接纳。");
}
/** 集合比较拒绝重复、遗漏和多余项，顺序不影响授权关系含义。 */
function sameIds(
  actual: unknown,
  expected: readonly string[] | undefined,
): boolean {
  return (
    Array.isArray(actual) &&
    actual.every((id) => typeof id === "string") &&
    new Set(actual.map((id: string) => id.toLowerCase())).size ===
      actual.length &&
    JSON.stringify([...actual].map((id: string) => id.toLowerCase()).sort()) ===
      JSON.stringify([...(expected ?? [])].map((id) => id.toLowerCase()).sort())
  );
}
/** 恢复文件只输出核实白名单；密码和任意响应正文即使出现也不能导出。 */
export function settingsRecoveryRecord(
  recordId: string,
  frozen: FrozenSettingsSubmission,
  evidence: SettingsSaveEvidence,
) {
  const values = frozen.values;
  const credentialOperation =
    frozen.kind === "user-create" || frozen.kind.startsWith("password-");
  const response = "response" in evidence ? evidence.response : undefined;
  const envelope = record(response?.data);
  const error = record(envelope?.error);
  const facts =
    evidence.outcome === "confirmed"
      ? getRequestFacts(evidence.response)
      : evidence.facts;
  return {
    recordId,
    businessType: "SystemSettings",
    operation: frozen.kind,
    apiOperation: facts ? { method: facts.method, path: facts.path } : null,
    targetId: frozen.targetId || null,
    baselineVersion: frozen.version || null,
    submittedAt: frozen.submittedAt,
    attemptId: frozen.attemptId,
    submitted: {
      userName: values.userName,
      nickName: values.nickName,
      email: values.email,
      phoneNumber: values.phoneNumber,
      roleCode: values.roleCode,
      roleName: values.roleName,
      description: values.description,
      ids: values.ids,
      isActive: values.isActive,
      confirmed: values.confirmed,
      ...(credentialOperation ? { credentialContentOmitted: true } : {}),
    },
    response: response
      ? {
          status: response.status,
          success: envelope?.success,
          error: { code: error?.code, field: error?.field },
        }
      : null,
    tracking:
      evidence.outcome !== "confirmed"
        ? {
            phase: facts?.phase,
            correlationId: facts?.correlationId,
          }
        : null,
    pendingQuestions: [
      "服务器是否提交仍未确认。",
      ...(credentialOperation
        ? [
            "凭据内容不允许保全；由现有合法业务/运维渠道核实账号或凭据结果，不能重发密码。",
          ]
        : []),
      "人工终止只释放本地草稿，不证明回滚或可安全重发。",
    ],
  };
}
/** 只接受对象形态，不按生成器可选字段断言任意正文可靠。 */
function record(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

/** 读取反馈使用只读语义，取消读取不提示服务器写入结果未知。 */
export function describeSettingsReadError(error: unknown): string {
  if (error instanceof RequestTransportError)
    return error.kind === "cancelled"
      ? "读取已取消，请重新读取。"
      : "系统设置读取失败，请检查网络后重试。";
  if (error instanceof ApiResponseError) {
    const body = record(error.response.data);
    const detail = record(body?.error);
    if (typeof detail?.message === "string") return detail.message;
  }
  return error instanceof Error ? error.message : "系统设置读取失败。";
}
