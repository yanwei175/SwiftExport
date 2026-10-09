import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { mockBrowserSession, mockJwtToken } from "./会话模拟";

test.use({ viewport: { width: 1400, height: 900 } });

/** 只精确模拟认证，偏好保存、重载和降级均操作真实浏览器 Storage。 */
async function signIn(page: Page) {
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page.getByLabel("密码", { exact: true }).fill("preferences-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
}

test("手动侧栏偏好在原JWT恢复后保留，响应式挂载不覆盖偏好", async ({
  page,
}) => {
  const fixture = await mockBrowserSession(page);
  await page.goto("/");
  await signIn(page);
  await page.getByRole("button", { name: "折叠导航", exact: true }).click();
  await expect(page.locator(".brand")).toHaveText("SE");
  const values = await page.evaluate(() => Object.values(localStorage));
  expect(values).toHaveLength(1);
  expect(values.join()).not.toContain(fixture.token);
  expect(values.join()).not.toMatch(/access_token|permissions|password/);
  await page.reload();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  await expect(page.locator(".brand")).toHaveText("SE");
  expect(fixture.errors).toEqual([]);
});

test("完成退出后另一用户不继承偏好，再登录原用户仍加载其偏好", async ({
  page,
}) => {
  const fixture = await mockBrowserSession(page);
  await page.goto("/");
  await signIn(page);
  await page.getByRole("button", { name: "折叠导航", exact: true }).click();
  await page.getByRole("button", { name: "退出登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  fixture.identity.userId = "user-b";
  fixture.token = mockJwtToken("user-b");
  await signIn(page);
  await expect(page.locator(".brand")).toHaveText("SwiftExport");
  await page.getByRole("button", { name: "退出登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  fixture.identity.userId = "user-a";
  fixture.token = mockJwtToken("user-a");
  await signIn(page);
  await expect(page.locator(".brand")).toHaveText("SE");
  expect(fixture.errors).toEqual([]);
});

test("Storage禁用时只提示一次并降级内存，编辑与退出仍可用", async ({
  page,
}) => {
  await page.addInitScript(() => {
    Object.defineProperty(window, "localStorage", {
      get() {
        throw new DOMException("disabled", "SecurityError");
      },
    });
  });
  const fixture = await mockBrowserSession(page);
  await page.goto("/");
  await signIn(page);
  await expect(
    page.getByText("界面偏好暂不能保存，当前页面仍可正常使用。", {
      exact: true,
    }),
  ).toHaveCount(1);
  await page.getByRole("button", { name: "折叠导航", exact: true }).click();
  await expect(page.locator(".brand")).toHaveText("SE");
  await page.getByRole("button", { name: "折叠导航", exact: true }).click();
  await expect(page.locator(".brand")).toHaveText("SwiftExport");
  await page.getByRole("button", { name: "退出登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "登录", exact: true }),
  ).toBeVisible();
  expect(fixture.errors).toEqual([]);
});
