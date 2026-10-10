import { expect } from "@playwright/test";
import type { Page } from "@playwright/test";
import { authenticationCalls, mockBrowserSession } from "./会话模拟";
import type {
  ManagedUser,
  ManagedRole,
  ManagedPermission,
} from "../../app/features/system/rbac/queries";

/** 系统设置真实生成请求的精确模拟，不写运行数据库或自动授予真实账号权限。 */
export const settingsPermissions = [
  "System.Users.View",
  "System.Users.Manage",
  "System.Roles.View",
  "System.Roles.Manage",
  "System.Permissions.View",
  "System.Dictionary.View",
];
/** 测试实体标识保持合法且可复现，绝不是客户端幂等键。 */
export const settingsUid = (index: number) =>
  `12345678-1234-4321-8765-${index.toString(16).padStart(12, "0")}`;

/** 每个浏览器有独立服务器状态，失败模式不伪造回滚能力。 */
export async function setupSettings(
  page: Page,
  permissions = settingsPermissions,
) {
  const session = await mockBrowserSession(page, {
    userId: settingsUid(1),
    permissions: [...permissions],
  });
  const fixture = {
    session,
    users: [
      {
        uid: settingsUid(1),
        version: 3,
        userName: "admin",
        nickName: "管理员",
        email: "admin@example.com",
        phoneNumber: "123",
        isActive: true,
        isAdmin: true,
      },
      {
        uid: settingsUid(2),
        version: 5,
        userName: "worker",
        nickName: "业务用户",
        email: "worker@example.com",
        phoneNumber: "456",
        isActive: true,
        isAdmin: false,
      },
    ] as ManagedUser[],
    roles: [
      {
        uid: settingsUid(11),
        version: 4,
        roleCode: "SYSTEM_ADMIN",
        roleName: "系统管理员",
        description: "内置角色",
        isActive: true,
        isSystem: true,
      },
      {
        uid: settingsUid(12),
        version: 7,
        roleCode: "SALES_OPERATOR",
        roleName: "业务操作员",
        description: "原描述",
        isActive: true,
        isSystem: false,
      },
      {
        uid: settingsUid(13),
        version: 2,
        roleCode: "OLD_ROLE",
        roleName: "停用角色",
        description: "",
        isActive: false,
        isSystem: false,
      },
    ] as ManagedRole[],
    permissions: [
      {
        uid: settingsUid(21),
        permissionKey: "Products.Product.View",
        displayName: "查看产品",
        groupKey: "Products",
        sortOrder: 1,
        isActive: true,
        isAssignable: true,
      },
      {
        uid: settingsUid(22),
        permissionKey: "Products.Product.Edit",
        displayName: "编辑产品",
        groupKey: "Products",
        sortOrder: 2,
        isActive: true,
        isAssignable: true,
      },
      {
        uid: settingsUid(23),
        permissionKey: "System.Users.View",
        displayName: "用户管理查看",
        groupKey: "System",
        sortOrder: 1,
        isActive: true,
        isAssignable: false,
      },
    ] as ManagedPermission[],
    userRoles: {
      [settingsUid(1)]: [settingsUid(11)],
      [settingsUid(2)]: [settingsUid(12)],
    } as Record<string, string[]>,
    rolePermissions: {
      [settingsUid(11)]: [settingsUid(21), settingsUid(22), settingsUid(23)],
      [settingsUid(12)]: [settingsUid(21)],
    } as Record<string, string[]>,
    writes: [] as {
      path: string;
      method: string;
      body: Record<string, unknown> | null;
      version?: string | null;
    }[],
    mode: "success" as
      "success" | "business-unknown" | "response-lost" | "incomplete",
    wrongPassword: false,
    readGate: undefined as Promise<void> | undefined,
    writeGate: undefined as Promise<void> | undefined,
    readStatus: 200,
    unexpected: [] as string[],
  };
  page.on("console", (entry) => {
    if (/Instance created by `useForm` is not connected/.test(entry.text()))
      session.errors.push(entry.text());
  });
  await page.route(
    (url) =>
      url.pathname.startsWith("/api/system/") ||
      [
        "/api/auth/register",
        "/api/auth/change-password",
        "/api/auth/admin/reset-password",
      ].includes(url.pathname),
    async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      const path = url.pathname;
      const method = request.method();
      expect(request.headers().authorization).toBe("Bearer " + session.token);
      const body = request.postData()
        ? (request.postDataJSON() as Record<string, unknown>)
        : null;
      if (body)
        expect(request.headers()["content-type"]).toBe("application/json");
      const envelope = (data: unknown) =>
        route.fulfill({ json: { success: true, data } });
      if (path.endsWith("/page") && method === "POST") {
        const rows = path.includes("/users/") ? fixture.users : fixture.roles;
        const index = Number(body?.pageIndex);
        const size = Number(body?.pageSize);
        const filtered = rows.filter(
          (row) =>
            (body?.isActive == null || row.isActive === body.isActive) &&
            JSON.stringify(row)
              .toLowerCase()
              .includes(String(body?.keyword ?? "").toLowerCase()),
        );
        return envelope({
          items: filtered.slice((index - 1) * size, index * size),
          totalCount: filtered.length,
          pageIndex: index,
          pageSize: size,
        });
      }
      const userId = path.match(/^\/api\/system\/users\/([^/]+)/)?.[1];
      const roleId = path.match(/^\/api\/system\/roles\/([^/]+)/)?.[1];
      const user = fixture.users.find((item) => item.uid === userId);
      const role = fixture.roles.find((item) => item.uid === roleId);
      if (method === "GET") {
        await fixture.readGate;
        if (fixture.readStatus !== 200)
          return route.fulfill({
            status: fixture.readStatus,
            json: {
              success: false,
              error: { code: "UNEXPECTED", message: "模拟读取失败" },
            },
          });
        if (path === "/api/system/permissions")
          return envelope(fixture.permissions);
        if (user && path.endsWith("/roles"))
          return envelope({
            userId,
            version: user.version,
            roleIds: fixture.userRoles[userId!] ?? [],
          });
        if (user && path.endsWith("/effective-permissions"))
          return envelope(["Products.Product.View"]);
        if (role && path.endsWith("/permissions"))
          return envelope({
            roleId,
            version: role.version,
            permissionIds: fixture.rolePermissions[roleId!] ?? [],
            isAutomatic: role.isSystem,
          });
        if (user && path === `/api/system/users/${userId}`)
          return envelope(user);
        if (role && path === `/api/system/roles/${roleId}`)
          return envelope(role);
      }
      const writePaths = [
        "/api/auth/register",
        "/api/auth/change-password",
        "/api/auth/admin/reset-password",
        "/api/system/roles",
      ];
      if (
        ["POST", "PUT", "DELETE"].includes(method) &&
        (writePaths.includes(path) || user || role)
      ) {
        fixture.writes.push({
          path,
          method,
          body,
          version: url.searchParams.get("expectedVersion"),
        });
        await fixture.writeGate;
        if (fixture.wrongPassword && path === "/api/auth/change-password")
          return route.fulfill({
            status: 422,
            json: {
              success: false,
              error: {
                code: "BUSINESS_BLOCKED",
                field: "CurrentPassword",
                message: "旧密码不正确。",
              },
            },
          });
        if (fixture.mode === "response-lost")
          return route.abort("connectionreset");
        if (fixture.mode === "business-unknown")
          return route.fulfill({
            status: 422,
            json: {
              success: false,
              error: {
                code: "BUSINESS_BLOCKED",
                message: "模拟拒绝：无法确认是否已写入",
                field: "NickName",
              },
            },
          });
        if (fixture.mode === "incomplete")
          return envelope({ uid: settingsUid(99), version: 1 });
        if (
          path === "/api/auth/change-password" ||
          path === "/api/auth/admin/reset-password"
        )
          return envelope(true);
        if (path === "/api/auth/register") {
          const created = {
            uid: settingsUid(100),
            version: 1,
            userName: body!.userName as string,
            nickName: (body!.nickName || body!.userName) as string,
            email: body!.email as string,
            phoneNumber: body!.phoneNumber as string,
            isActive: true,
            isAdmin: false,
          };
          fixture.users.push(created);
          return envelope(created.uid);
        }
        if (path === "/api/system/roles") {
          const created = {
            uid: settingsUid(101),
            version: 1,
            roleCode: body!.roleCode as string,
            roleName: body!.roleName as string,
            description: body!.description as string,
            isActive: true,
            isSystem: false,
          };
          fixture.roles.push(created);
          return envelope(created);
        }
        const target = user ?? role!;
        expect(
          Number(
            body?.expectedVersion ?? url.searchParams.get("expectedVersion"),
          ),
        ).toBe(target.version);
        target.version = target.version! + 1;
        if (user && path.endsWith("/roles")) {
          fixture.userRoles[userId!] = body!.roleIds as string[];
          return envelope({
            userId,
            version: user.version,
            roleIds: fixture.userRoles[userId!],
          });
        }
        if (role && path.endsWith("/permissions")) {
          fixture.rolePermissions[roleId!] = body!.permissionIds as string[];
          return envelope({
            roleId,
            version: role.version,
            permissionIds: fixture.rolePermissions[roleId!],
            isAutomatic: false,
          });
        }
        if (method === "DELETE") {
          fixture.roles = fixture.roles.filter((item) => item.uid !== roleId);
          return envelope(true);
        }
        if (path.endsWith("/status")) {
          target.isActive = body!.isActive as boolean;
          return envelope(target);
        }
        if (user) {
          user.nickName = body!.nickName as string;
          user.email = body!.email as string;
          user.phoneNumber = body!.phoneNumber as string;
          return envelope(user);
        }
        if (role) {
          role.roleName = body!.roleName as string;
          role.description = body!.description as string;
          return envelope(role);
        }
      }
      fixture.unexpected.push(method + " " + path);
      await route.abort("blockedbyclient");
    },
  );
  await page.goto("/login");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page
    .getByLabel("密码", { exact: true })
    .fill("settings-browser-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  return fixture;
}

/** URL 打开消费真实页面目录，不直接挂假组件；选择原始实体目标不会走全局活动参数。 */
export async function openSettings(page: Page, view: string, id?: string) {
  await page.goto(`/workspace?view=${view}${id ? `&id=${id}` : ""}`);
  const pane = page.locator(
    `[data-testid="${view.includes("-management") || view === "permission-catalog" ? view : `settings-editor-${view}`}"]:visible`,
  );
  await expect(pane).toBeVisible();
  return pane;
}

/** 结束时确认没有意外请求或生命周期错误，认证调用不混入业务写入计数。 */
export function verifySettingsFixture(
  fixture: Awaited<ReturnType<typeof setupSettings>>,
) {
  expect(fixture.unexpected).toEqual([]);
  expect(fixture.session.errors).toEqual([]);
  expect(
    fixture.session.apiCalls
      .filter((call) => !authenticationCalls.includes(call))
      .every((call) =>
        /\/api\/(system\/|auth\/(register|change-password|admin\/reset-password))/.test(
          call,
        ),
      ),
  ).toBe(true);
}
