import { afterEach, describe, expect, it, vi } from "vitest";
import * as client from "../../app/shared/api/generated/client";
import { RequestTransportError } from "../../app/shared/api/请求执行";
import {
  classifySettingsResponse,
  freezeSettingsSubmission,
  initialSettingsValues,
  projectSettingsValues,
  settingsRecoveryRecord,
  submitSettings,
  validateSettingsValues,
} from "../../app/features/system/rbac/提交契约";
import { validSettingsTarget } from "../../app/features/system/rbac/页面定义";
const id = "12345678-1234-4321-8765-000000000001";
const other = "12345678-1234-4321-8765-000000000002";
const response = (data: unknown, status = 200) => ({
  status,
  data,
  headers: new Headers(),
});
afterEach(() => vi.restoreAllMocks());

describe("系统设置冻结和完整回执", () => {
  it("错误旧密码只在指定端点、状态、代码和字段全部匹配时归 B", () => {
    const frozen = freezeSettingsSubmission("password-change", "", 0, {});
    const body = {
      success: false,
      error: {
        code: "BUSINESS_BLOCKED",
        field: "CurrentPassword",
        message: "旧密码不正确。",
      },
    };
    expect(classifySettingsResponse(frozen, response(body, 422)).outcome).toBe(
      "not-applied",
    );
    expect(classifySettingsResponse(frozen, response(body, 500)).outcome).toBe(
      "unknown",
    );
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("password-reset", id, 3, {}),
        response(body, 422),
      ).outcome,
    ).toBe("unknown");
    expect(
      classifySettingsResponse(
        frozen,
        response({ ...body, error: { ...body.error, field: "Other" } }, 422),
      ).outcome,
    ).toBe("unknown");
  });
  it("Form 归一化比较还原，关系顺序不产生 dirty，冻结无别名", () => {
    const values = { ids: [other, id] };
    const frozen = freezeSettingsSubmission("user-roles", id, 3, values);
    values.ids.pop();
    expect(frozen.values.ids).toHaveLength(2);
    expect(
      projectSettingsValues("user-profile", {
        nickName: " 原名称 ",
        email: "",
      }),
    ).toEqual(
      initialSettingsValues("user-profile", {
        user: { nickName: "原名称", email: null },
      }),
    );
    expect(projectSettingsValues("user-roles", { ids: [id, other] })).toEqual(
      frozen.values,
    );
    expect(Object.isFrozen(frozen.values.ids)).toBe(true);
  });
  it("完整用户资料和版本才允许接纳，成功标记不能替代字段核对", () => {
    const frozen = freezeSettingsSubmission("user-profile", id, 3, {
      nickName: "新名称",
      email: "a@example.com",
      phoneNumber: "123",
    });
    const data = {
      uid: id,
      version: 4,
      userName: "worker",
      isActive: true,
      isAdmin: false,
      nickName: "新名称",
      email: "a@example.com",
      phoneNumber: "123",
    };
    expect(
      classifySettingsResponse(frozen, response({ success: true, data }))
        .outcome,
    ).toBe("confirmed");
    for (const patch of [
      { uid: other },
      { version: 3 },
      { nickName: "旧名称" },
      { phoneNumber: null },
    ])
      expect(
        classifySettingsResponse(
          frozen,
          response({ success: true, data: { ...data, ...patch } }),
        ).outcome,
      ).toBe("unknown");
  });
  it("整组关系拒绝遗漏、多余和重复；空集合可以明确撤销全部", () => {
    const frozen = freezeSettingsSubmission("role-permissions", id, 7, {
      ids: [id, other],
    });
    const data = {
      roleId: id,
      version: 8,
      isAutomatic: false,
      permissionIds: [other, id],
    };
    expect(
      classifySettingsResponse(frozen, response({ success: true, data }))
        .outcome,
    ).toBe("confirmed");
    for (const ids of [[id], [id, id], [id, other, other]])
      expect(
        classifySettingsResponse(
          frozen,
          response({ success: true, data: { ...data, permissionIds: ids } }),
        ).outcome,
      ).toBe("unknown");
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("user-roles", id, 3, { ids: [] }),
        response({
          success: true,
          data: { userId: id, version: 4, roleIds: [] },
        }),
      ).outcome,
    ).toBe("confirmed");
  });
  it("创建、布尔回执及状态使用各自契约，不能接纳假 GUID 或错误状态", () => {
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("user-create", "", 0, {}),
        response({ success: true, data: id }),
      ).outcome,
    ).toBe("confirmed");
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("user-create", "", 0, {}),
        response({ success: true, data: "" }),
      ).outcome,
    ).toBe("unknown");
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("user-create", "", 0, {}),
        response({ success: true, data: "-".repeat(36) }),
      ).outcome,
    ).toBe("unknown");
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("password-change", "", 0, {}),
        response({ success: true, data: false }),
      ).outcome,
    ).toBe("unknown");
    expect(
      classifySettingsResponse(
        freezeSettingsSubmission("user-status", id, 3, { isActive: false }),
        response({
          success: true,
          data: { uid: id, version: 4, isActive: true },
        }),
      ).outcome,
    ).toBe("unknown");
  });
  it.each([400, 401, 403, 422, 429, 500, 503])(
    "HTTP %i 的已发失败不统一解锁，不分析中文 message",
    (status) => {
      const frozen = freezeSettingsSubmission("user-profile", id, 3, {});
      expect(
        classifySettingsResponse(
          frozen,
          response(
            {
              success: false,
              error: {
                code: "VERSION_CONFLICT",
                message: "已回滚，未提交变更",
              },
            },
            status,
          ),
        ).outcome,
      ).toBe("unknown");
    },
  );
  it("确定未发送归 B；已派发的网络错误归 C，恢复保留实际接口操作", async () => {
    const frozen = freezeSettingsSubmission("user-profile", id, 3, {});
    const operation = vi.spyOn(client, "updateManagedUser");
    const facts = {
      path: `/api/system/users/${id}`,
      method: "PUT",
      phase: "not-sent" as const,
      credentialRevision: 0,
    };
    operation.mockRejectedValueOnce(
      new RequestTransportError("not-sent", facts),
    );
    expect((await submitSettings(frozen)).outcome).toBe("not-applied");
    operation.mockRejectedValueOnce(
      new RequestTransportError("network", { ...facts, phase: "dispatched" }),
    );
    const unknown = await submitSettings(frozen);
    expect(unknown.outcome).toBe("unknown");
    const record = settingsRecoveryRecord("record-network", frozen, unknown);
    expect(record.apiOperation).toEqual({ path: facts.path, method: "PUT" });
    expect(record.tracking?.phase).toBe("dispatched");
  });
  it("导出不含凭据、任意响应内容或错误 message，文件记录保留本地关联与未确认事项", () => {
    const frozen = freezeSettingsSubmission("user-create", "", 0, {
      userName: "newuser",
      password: "SecretPassword",
      confirmPassword: "SecretPassword",
    });
    const result = classifySettingsResponse(
      frozen,
      response(
        {
          success: false,
          error: { code: "UNEXPECTED", message: "secret-in-message" },
          data: { token: "secret-in-body" },
        },
        500,
      ),
    );
    const record = settingsRecoveryRecord("record-a", frozen, result);
    const text = JSON.stringify(record);
    for (const forbidden of [
      "SecretPassword",
      "secret-in-message",
      "secret-in-body",
      "Authorization",
    ])
      expect(text).not.toContain(forbidden);
    expect(record.recordId).toBe("record-a");
    expect(record.attemptId).toBe(frozen.attemptId);
    expect(record.submitted.credentialContentOmitted).toBe(true);
    expect(text).not.toContain("commandId");
  });
  it("真实目标、候选权限及不可变内置边界不能从 URL 或选值绕过", () => {
    expect(validSettingsTarget("role-profile", { id })).toBe(true);
    expect(validSettingsTarget("role-profile", {})).toBe(false);
    expect(validSettingsTarget("user-create", { id })).toBe(false);
    expect(validSettingsTarget("password-change", { id })).toBe(false);
    expect(
      validateSettingsValues(
        "role-delete",
        { confirmed: true },
        { role: { isSystem: true } },
      ),
    ).toContain("内置");
    expect(
      validateSettingsValues(
        "password-reset",
        {},
        { user: { isActive: true, isAdmin: true } },
      ),
    ).toContain("普通用户");
    expect(
      validateSettingsValues(
        "user-roles",
        { ids: [id] },
        { user: { isActive: true }, roles: [{ uid: id, isActive: false }] },
      ),
    ).toContain("有效角色");
    expect(
      validateSettingsValues(
        "role-permissions",
        { ids: [id] },
        { permissions: [{ uid: id, isActive: true, isAssignable: false }] },
      ),
    ).toContain("可分配");
  });
});
