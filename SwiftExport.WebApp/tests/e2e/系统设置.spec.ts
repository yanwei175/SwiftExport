import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import {
  openSettings,
  setupSettings,
  settingsPermissions,
  settingsUid as uid,
  verifySettingsFixture,
} from "./系统设置模拟";
import type { Locator, Page } from "@playwright/test";
const observations = new WeakMap<
  Page,
  Awaited<ReturnType<typeof setupSettings>>
>();
const editor = (page: Page, kind: string) =>
  page.locator(`[data-testid="settings-editor-${kind}"]:visible`);
test.afterEach(async ({ page }) => {
  const fixture = observations.get(page);
  if (fixture) verifySettingsFixture(fixture);
});

test("两个实体表单 DOM 身份独立，迟到隐藏读取取消后可重新读取，后台不覆盖草稿", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const list = await openSettings(page, "user-management");
  let release!: () => void;
  fixture.readGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const row = list
    .getByRole("row")
    .filter({ has: page.getByRole("cell", { name: "worker", exact: true }) });
  await row.getByRole("button", { name: "资料", exact: true }).click();
  const profile = editor(page, "user-profile");
  await expect(profile).toBeVisible();
  await page.getByRole("menuitem", { name: /首页/ }).click();
  fixture.readGate = undefined;
  release();
  await page.getByRole("tab", { name: "用户资料", exact: true }).click();
  await expect(profile.getByLabel("显示名称")).toHaveValue("业务用户");
  await profile.getByLabel("显示名称").fill("隐藏后保留");
  await page.getByRole("tab", { name: "用户管理", exact: true }).click();
  const admin = list
    .getByRole("row")
    .filter({ has: page.getByRole("cell", { name: "admin", exact: true }) });
  await admin.getByRole("button", { name: "资料", exact: true }).click();
  const adminProfile = editor(page, "user-profile");
  await expect(adminProfile.getByLabel("显示名称")).toHaveValue("管理员");
  const ids = await page
    .locator(
      '[data-testid="settings-editor-user-profile"] input[id$="nickName"]',
    )
    .evaluateAll((inputs) => inputs.map((input) => input.id));
  expect(new Set(ids).size).toBe(2);
  await page.getByRole("tab", { name: "用户资料 *", exact: true }).click();
  await expect(profile.getByLabel("显示名称")).toHaveValue("隐藏后保留");
  expect(fixture.writes).toHaveLength(0);
});

test("已验证旧密码短路仅保留输入并解锁，修正后可再次提交", async ({ page }) => {
  const fixture = await initialize(page, []);
  fixture.wrongPassword = true;
  const pane = await openSettings(page, "password-change");
  await expect(pane.getByLabel("旧密码")).toBeEnabled();
  await pane.getByLabel("旧密码").fill("WrongSecret123");
  await pane.getByLabel("新密码").fill("NewSecret123");
  await pane.getByLabel("确认密码").fill("NewSecret123");
  await save(pane);
  await expect(pane).toContainText("旧密码不正确。");
  await expect(pane.getByLabel("旧密码")).toBeEnabled();
  await expect(pane.getByLabel("新密码")).toHaveValue("NewSecret123");
  await expect(pane.getByRole("button", { name: "导出恢复记录" })).toHaveCount(
    0,
  );
  fixture.wrongPassword = false;
  await pane.getByLabel("旧密码").fill("CorrectSecret123");
  await save(pane);
  await expect(pane).toContainText("密码已修改");
  expect(fixture.writes).toHaveLength(2);
});

test("恢复导出失败不能终止；原始输入和冻结状态仍保留", async ({ page }) => {
  const fixture = await initialize(page);
  fixture.mode = "response-lost";
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await pane.getByLabel("显示名称").fill("导出失败仍保护");
  await save(pane);
  await expect(pane).toContainText("保存结果未知");
  await page.evaluate(() => {
    URL.createObjectURL = () => {
      throw new Error("浏览器测试模拟导出失败");
    };
  });
  await pane.getByRole("button", { name: "导出恢复记录" }).click();
  await expect(
    page.getByText("恢复记录导出失败，继续保留恢复限制。", { exact: true }),
  ).toBeVisible();
  await pane.getByRole("button", { name: "终止本地恢复" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(pane.getByLabel("显示名称")).toHaveValue("导出失败仍保护");
  await expect(
    pane.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
  expect(fixture.writes).toHaveLength(1);
});
/** 所有场景先认证，再经正式页面目录与生成请求链交互。 */
async function initialize(page: Page, permissions = settingsPermissions) {
  const fixture = await setupSettings(page, permissions);
  observations.set(page, fixture);
  return fixture;
}
/** 原生 Ant 多选仅筛选显示；测试通过真实选项点击，不能绕过表单当前值 owner。 */
async function select(page: Page, combobox: Locator, label: string) {
  await combobox.click();
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText(label, { exact: true })
    .click();
  await combobox.press("Escape");
}
async function save(pane: Locator) {
  await pane.getByRole("button", { name: "保存", exact: true }).click();
}

test("系统设置菜单按各自权限显示，普通用户仍有个人设置", async ({ page }) => {
  await initialize(page, []);
  await page.getByRole("menuitem", { name: /系统设置/ }).click();
  await expect(
    page.getByRole("menuitem", { name: "个人设置", exact: true }),
  ).toBeVisible();
  for (const name of ["用户管理", "角色管理", "权限定义", "字典管理"])
    await expect(page.getByRole("menuitem", { name, exact: true })).toHaveCount(
      0,
    );
  await page.getByRole("menuitem", { name: "个人设置", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "修改我的密码" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "刷新我的权限" }).click();
  await expect(
    page.getByText("权限已从服务器刷新。", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "修改我的密码" }).click();
  await expect(editor(page, "password-change")).toBeVisible();
});

test("用户列表搜索、状态与服务端分页不丢掉后续页面", async ({ page }) => {
  const fixture = await initialize(page);
  for (let index = 30; index < 55; index++)
    fixture.users.push({
      uid: uid(index),
      version: 1,
      userName: `user${index}`,
      nickName: `用户${index}`,
      isActive: false,
      isAdmin: false,
    });
  const pane = await openSettings(page, "user-management");
  await expect(
    pane.getByRole("cell", { name: "admin", exact: true }),
  ).toBeVisible();
  await pane.locator(".ant-pagination-next").click();
  await expect(
    pane.getByRole("cell", { name: "user54", exact: true }),
  ).toBeVisible();
  await pane.getByLabel("用户搜索").fill("worker");
  await pane.getByLabel("用户搜索").press("Enter");
  await expect(
    pane.getByRole("cell", { name: "worker", exact: true }),
  ).toBeVisible();
  await expect(
    pane.getByRole("cell", { name: "admin", exact: true }),
  ).toHaveCount(0);
  await select(
    page,
    pane.getByRole("combobox", { name: "用户状态筛选" }),
    "停用",
  );
  await expect(pane).toContainText("暂无数据");
});

test("创建用户走生成 register 操作，校验阻断无效密码，成功清凭据并能打开记录", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const list = await openSettings(page, "user-management");
  await list.getByRole("button", { name: "创建用户", exact: true }).click();
  const pane = editor(page, "user-create");
  await pane.getByLabel("登录名", { exact: true }).fill("newuser");
  await pane.getByLabel("密码", { exact: true }).fill("short");
  await pane.getByLabel("确认密码", { exact: true }).fill("different");
  await save(pane);
  await expect(pane).toContainText("字段校验未通过");
  expect(fixture.writes).toHaveLength(0);
  await pane.getByLabel("密码", { exact: true }).fill("Secret123");
  await pane.getByLabel("确认密码", { exact: true }).fill("Secret123");
  await pane.getByLabel("显示名称", { exact: true }).fill("新用户");
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]).toMatchObject({
    path: "/api/auth/register",
    method: "POST",
    body: {
      userName: "newuser",
      nickName: "新用户",
      password: "Secret123",
      confirmPassword: "Secret123",
    },
  });
  await expect(pane.getByLabel("密码", { exact: true })).toHaveValue("");
  await expect(
    pane.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
  await pane.getByRole("button", { name: "打开已创建记录" }).click();
  await expect(editor(page, "user-profile").getByLabel("显示名称")).toHaveValue(
    "新用户",
  );
});

test("用户资料版本保存、还原与后台重新显示不会覆盖草稿", async ({ page }) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await pane.getByLabel("显示名称").fill("新业务用户");
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    expectedVersion: 5,
    nickName: "新业务用户",
    email: "worker@example.com",
    phoneNumber: "456",
  });
  await pane.getByLabel("显示名称").fill("新业务用户2");
  await page.getByRole("menuitem", { name: /首页/ }).click();
  const tab = page
    .locator(".ant-tabs-tab")
    .filter({ has: page.getByRole("tab", { name: /用户资料/ }) });
  await tab.getByRole("tab").click();
  await expect(pane.getByLabel("显示名称")).toHaveValue("新业务用户2");
  await save(pane);
  await expect.poll(() => fixture.writes.length).toBe(2);
  expect(fixture.writes[1]?.body?.expectedVersion).toBe(6);
});

test("首次无效 Form 输入、隐藏关闭与全部关闭仍保护；改回原值可直接关闭", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await pane.getByLabel("显示名称").fill("");
  const tab = page
    .locator(".ant-tabs-tab")
    .filter({ has: page.getByRole("tab", { name: /用户资料/ }) });
  await page.getByRole("menuitem", { name: /首页/ }).click();
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并关闭", exact: true })
    .click();
  await expect(pane).toContainText("字段校验未通过");
  await expect(pane.getByLabel("显示名称")).toHaveValue("");
  expect(fixture.writes).toHaveLength(0);
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await pane.getByLabel("显示名称").fill("业务用户");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(pane).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("用户角色使用 Ant 多选提交完整集合，清空显式撤销全部，读取有效权限", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "user-roles", uid(2));
  await expect(pane).toContainText("Products.Product.View");
  const combo = pane.getByRole("combobox", { name: "角色（完整集合）" });
  await select(page, combo, "系统管理员 · SYSTEM_ADMIN");
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    expectedVersion: 5,
    roleIds: [uid(11), uid(12)],
  });
  await pane.getByRole("button", { name: "Clear", exact: true }).click();
  await save(pane);
  await expect.poll(() => fixture.writes.length).toBe(2);
  expect(fixture.writes[1]?.body).toEqual({ expectedVersion: 6, roleIds: [] });
});

test("缺角色候选读权时不请求目录，保留已有关系并禁用编辑", async ({ page }) => {
  const fixture = await initialize(page, [
    "System.Users.View",
    "System.Users.Manage",
  ]);
  const pane = await openSettings(page, "user-roles", uid(2));
  await expect(pane).toContainText("当前关系，候选不可用");
  await expect(pane.getByRole("combobox")).toBeDisabled();
  await expect(
    pane.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
  expect(fixture.session.apiCalls).not.toContain("POST /api/system/roles/page");
});

test("仅查看权限允许读取资料，写入与管理员密码重置均不能放行", async ({
  page,
}) => {
  const fixture = await initialize(page, ["System.Users.View"]);
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await expect(pane.getByLabel("显示名称")).toBeDisabled();
  expect(fixture.writes).toHaveLength(0);
  const reset = await openSettings(page, "password-reset", uid(1));
  await expect(reset).toContainText("管理员密码只能由本人修改");
  await expect(
    reset.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
});

test("用户状态提交真实目标版本，普通用户密码重置使用独立已有端点", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "user-status", uid(2));
  await expect(pane.getByRole("switch")).toBeEnabled();
  await pane.getByRole("switch").click();
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    expectedVersion: 5,
    isActive: false,
  });
  fixture.users[1]!.isActive = true;
  const reset = await openSettings(page, "password-reset", uid(2));
  await expect(reset.getByLabel("新密码", { exact: true })).toBeEnabled();
  await reset.getByLabel("新密码", { exact: true }).fill("Reset123");
  await reset.getByLabel("确认密码", { exact: true }).fill("Reset123");
  await save(reset);
  await expect(reset).toContainText("保存已确认成功");
  expect(fixture.writes[1]).toMatchObject({
    path: "/api/auth/admin/reset-password",
    body: {
      userId: uid(2),
      newPassword: "Reset123",
      confirmPassword: "Reset123",
    },
  });
});

test("创建普通角色校验稳定编码，资料修改不发送编码或系统标记", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const list = await openSettings(page, "role-management");
  await list.getByRole("button", { name: "创建角色", exact: true }).click();
  const pane = editor(page, "role-create");
  await pane.getByLabel("角色编码").fill("SYSTEM_ADMIN");
  await pane.getByLabel("角色名称").fill("新角色");
  await save(pane);
  await expect(pane).toContainText("不能使用内置角色编码");
  expect(fixture.writes).toHaveLength(0);
  await pane.getByLabel("角色编码").fill("WEB_OPERATOR");
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    roleCode: "WEB_OPERATOR",
    roleName: "新角色",
    description: "",
  });
  await pane.getByRole("button", { name: "打开已创建记录" }).click();
  const profile = editor(page, "role-profile");
  await expect(profile.getByLabel("角色名称")).toHaveValue("新角色");
  await profile.getByLabel("角色描述").fill("新说明");
  await save(profile);
  await expect.poll(() => fixture.writes.length).toBe(2);
  expect(fixture.writes[1]?.body).toEqual({
    expectedVersion: 1,
    roleName: "新角色",
    description: "新说明",
  });
});

test("角色权限搜索不裁剪完整集合，保留键禁选，清空支持全部撤销", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "role-permissions", uid(12));
  const combo = pane.getByRole("combobox", { name: "权限（完整集合）" });
  await expect(pane).toContainText("查看产品");
  await combo.fill("编辑");
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText("Products · 编辑产品 (Products.Product.Edit)", { exact: true })
    .click();
  await combo.press("Escape");
  await save(pane);
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    expectedVersion: 7,
    permissionIds: [uid(21), uid(22)],
  });
  await combo.click();
  const reserved = page
    .locator(".ant-select-dropdown:visible .ant-select-item-option")
    .filter({ hasText: "用户管理查看" });
  await expect(reserved).toHaveClass(/disabled/);
  await combo.press("Escape");
  await pane.getByRole("button", { name: "Clear", exact: true }).click();
  await save(pane);
  await expect.poll(() => fixture.writes.length).toBe(2);
  expect(fixture.writes[1]?.body).toEqual({
    expectedVersion: 8,
    permissionIds: [],
  });
});

test("系统角色只读且自动全目录，缺权限目录读权禁止分配", async ({ page }) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "role-permissions", uid(11));
  await expect(pane).toContainText("管理员自动拥有全部目录权限");
  await expect(
    pane.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
  const list = await openSettings(page, "role-management");
  const row = list.getByRole("row").filter({
    has: page.getByRole("cell", { name: "SYSTEM_ADMIN", exact: true }),
  });
  await expect(
    row.getByRole("button", { name: "删除", exact: true }),
  ).toBeDisabled();
  await expect(
    row.getByRole("button", { name: "停用", exact: true }),
  ).toBeDisabled();
  fixture.session.identity.permissions = [
    "System.Roles.View",
    "System.Roles.Manage",
  ];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  const normal = await openSettings(page, "role-permissions", uid(12));
  await expect(normal).toContainText("没有候选目录读取权限");
  await expect(
    normal.getByRole("button", { name: "保存", exact: true }),
  ).toBeDisabled();
  expect(fixture.writes).toHaveLength(0);
});

test("普通角色启停和删除保留 ExpectedVersion，删除必须主动确认", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const status = await openSettings(page, "role-status", uid(12));
  await expect(status.getByRole("switch")).toBeEnabled();
  await status.getByRole("switch").click();
  await save(status);
  await expect(status).toContainText("保存已确认成功");
  expect(fixture.writes[0]?.body).toEqual({
    expectedVersion: 7,
    isActive: false,
  });
  const remove = await openSettings(page, "role-delete", uid(12));
  await expect(remove.getByRole("checkbox")).toBeEnabled();
  await remove.getByRole("checkbox").check();
  await remove.getByRole("button", { name: "确认删除角色" }).click();
  await expect(remove).toContainText("保存已确认成功");
  expect(fixture.writes[1]).toMatchObject({
    method: "DELETE",
    path: `/api/system/roles/${uid(12)}`,
    version: "8",
  });
});

test("权限定义只读、分组搜索以及读取失败保留可解释重试", async ({ page }) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "permission-catalog");
  await expect(
    pane.getByRole("cell", { name: "Products.Product.View", exact: true }),
  ).toBeVisible();
  await pane.getByLabel("权限搜索").fill("编辑");
  await expect(
    pane.getByRole("cell", { name: "Products.Product.Edit", exact: true }),
  ).toBeVisible();
  await expect(
    pane.getByRole("cell", { name: "Products.Product.View", exact: true }),
  ).toHaveCount(0);
  fixture.readStatus = 500;
  await pane.getByRole("button", { name: "刷新权限定义" }).click();
  await expect(pane).toContainText("模拟读取失败");
  await expect(pane).not.toContainText("服务器结果仍需核实");
  fixture.readStatus = 200;
  await pane.getByRole("button", { name: "刷新权限定义" }).click();
  await expect(pane.getByRole("alert")).toHaveCount(0);
  expect(fixture.writes).toHaveLength(0);
});

for (const mode of ["business-unknown", "response-lost", "incomplete"] as const)
  test(`${mode} 不能接纳、重发、放弃或被全部关闭绕过`, async ({ page }) => {
    const fixture = await initialize(page);
    fixture.mode = mode;
    const pane = await openSettings(page, "user-profile", uid(2));
    await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
    await pane.getByLabel("显示名称").fill("待核实输入");
    await save(pane);
    await expect(pane).toContainText("保存结果未知");
    await expect(
      pane.getByRole("button", { name: "保存", exact: true }),
    ).toBeDisabled();
    await expect(
      pane.getByRole("button", { name: "放弃修改", exact: true }),
    ).toBeDisabled();
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(pane.getByLabel("显示名称")).toHaveValue("待核实输入");
    await expect(pane).toBeVisible();
    await pane.getByRole("button", { name: "终止本地恢复" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(fixture.writes).toHaveLength(1);
  });

test("导出取消和未确认均不终止，真实文件保全后两次确认关闭且不标记成功", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.mode = "response-lost";
  const pane = await openSettings(page, "password-reset", uid(2));
  await expect(pane.getByLabel("新密码", { exact: true })).toBeEnabled();
  await pane.getByLabel("新密码", { exact: true }).fill("NeverExportSecret123");
  await pane
    .getByLabel("确认密码", { exact: true })
    .fill("NeverExportSecret123");
  await save(pane);
  await expect(pane).toContainText("保存结果未知");
  const downloadEvent = page.waitForEvent("download");
  await pane.getByRole("button", { name: "导出恢复记录" }).click();
  const download = await downloadEvent;
  const content = await readFile((await download.path())!, "utf8");
  const record = JSON.parse(content);
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(content).not.toContain("NeverExportSecret123");
  expect(content).not.toContain(fixture.session.token);
  expect(record.submitted.credentialContentOmitted).toBe(true);
  expect(record.apiOperation).toEqual({
    method: "POST",
    path: "/api/auth/admin/reset-password",
  });
  await pane.getByRole("button", { name: "终止本地恢复" }).click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await expect(pane).toBeVisible();
  await pane.getByRole("button", { name: "终止本地恢复" }).click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "已妥善保留" })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认终止本地恢复" }),
  ).toContainText("服务器结果仍未确认");
  await page
    .getByRole("dialog", { name: "确认终止本地恢复" })
    .getByRole("button", { name: "终止并关闭" })
    .click();
  await expect(pane).toHaveCount(0);
  expect(fixture.writes).toHaveLength(1);
});

test("保存期间只读、可切换其他页，回执只接纳原实体草稿", async ({ page }) => {
  const fixture = await initialize(page);
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await pane.getByLabel("显示名称").fill("原标签保存内容");
  let release!: () => void;
  fixture.writeGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await save(pane);
  await expect(pane.getByLabel("显示名称")).toBeDisabled();
  await page.getByRole("menuitem", { name: /首页/ }).click();
  release();
  await page.getByRole("tab", { name: /用户资料/ }).click();
  await expect(pane.getByLabel("显示名称")).toHaveValue("原标签保存内容");
  await expect(pane).toContainText("保存已确认成功");
  expect(fixture.writes).toHaveLength(1);
});

test("撤销写权保留草稿且禁止恢复导出，撤读权遮蔽已打开页", async ({ page }) => {
  const fixture = await initialize(page);
  fixture.mode = "business-unknown";
  const pane = await openSettings(page, "user-profile", uid(2));
  await expect(pane.getByLabel("显示名称")).toHaveValue("业务用户");
  await pane.getByLabel("显示名称").fill("撤权前输入");
  await save(pane);
  await expect(pane).toContainText("保存结果未知");
  fixture.session.identity.permissions = ["System.Users.View"];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  await expect(pane).toContainText("没有此操作权限");
  const downloads: string[] = [];
  page.on("download", (download) =>
    downloads.push(download.suggestedFilename()),
  );
  await pane.getByRole("button", { name: "导出恢复记录" }).click();
  await expect(
    page.getByText("当前权限不允许导出，需现有合法业务/运维渠道介入。", {
      exact: true,
    }),
  ).toBeVisible();
  expect(downloads).toEqual([]);
  fixture.session.identity.permissions = [];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  await expect(pane).toHaveCount(0);
  await expect(
    page.getByText("当前没有页面读取权限", {
      exact: true,
    }),
  ).toBeVisible();
});

test("本人改密成功触发旧凭据重认证，其他页签草稿保留", async ({ page }) => {
  const fixture = await initialize(page);
  const profile = await openSettings(page, "user-profile", uid(2));
  await expect(profile.getByLabel("显示名称")).toHaveValue("业务用户");
  await profile.getByLabel("显示名称").fill("另页草稿");
  await page.getByRole("menuitem", { name: /系统设置/ }).click();
  await page.getByRole("menuitem", { name: "个人设置", exact: true }).click();
  await page.getByRole("button", { name: "修改我的密码" }).click();
  const pane = editor(page, "password-change");
  await pane.getByLabel("旧密码", { exact: true }).fill("OldSecret123");
  await pane.getByLabel("新密码", { exact: true }).fill("NewSecret123");
  await pane.getByLabel("确认密码", { exact: true }).fill("NewSecret123");
  fixture.session.meStatus = 401;
  await save(pane);
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  expect(fixture.writes[0]).toMatchObject({
    path: "/api/auth/change-password",
    body: {
      currentPassword: "OldSecret123",
      newPassword: "NewSecret123",
      confirmPassword: "NewSecret123",
    },
  });
  fixture.session.meStatus = 200;
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page.getByLabel("密码", { exact: true }).fill("NewSecret123");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(pane).toContainText("密码已修改");
  await page.getByRole("tab", { name: /用户资料/ }).click();
  await expect(profile.getByLabel("显示名称")).toHaveValue("另页草稿");
});
