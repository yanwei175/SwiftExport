import { installBrowserSessionProtocol, mockJwtToken } from "./会话模拟";
import { expect, test } from "@playwright/test";
const authToken = mockJwtToken("tester");
import type { Page } from "@playwright/test";

/** 真实页面消费公共编辑集合，业务请求全部阻断；只模拟已有 login/me。 */
async function openGrid(page: Page) {
  const calls: string[] = [];
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
      const path = new URL(route.request().url()).pathname;
      calls.push(path);
      if (path === "/api/auth/login") {
        await route.fulfill({
          json: { access_token: authToken },
        });
      } else if (path === "/api/auth/me") {
        await route.fulfill({
          json: {
            userId: "collection-user",
            userName: "tester",
            permissions: ["Products.Product.View", "Products.Product.Edit"],
          },
        });
      } else await route.abort("blockedbyclient");
    },
  );
  await installBrowserSessionProtocol(page, authToken);
  await page.goto("/login");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page
    .getByLabel("密码", { exact: true })
    .fill("collection-test-password");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  await page.getByRole("menuitem", { name: /骨架验收/ }).click();
  await page.getByRole("menuitem", { name: "列表验收", exact: true }).click();
  await expect(activeGrid(page)).toBeVisible();
  return { calls, errors };
}

/** 只读取活动展示；原始缓冲和行生命周期保留在 Activity 外的 feature 会话。 */
function activeGrid(page: Page) {
  return page.locator('[data-testid^="verification-"]:visible');
}

test("新增后删除不产生删除请求，未完成新行校验失败不提交", async ({ page }) => {
  const fixture = await openGrid(page);
  const grid = activeGrid(page);
  await grid.getByRole("button", { name: "新增行", exact: true }).click();
  await expect(
    grid.getByText("新增：1；修改：0；删除：0", { exact: true }),
  ).toBeVisible();
  await grid.getByRole("button", { name: "保存", exact: true }).click();
  await expect(
    grid.getByRole("textbox", { name: "第3行名称", exact: true }),
  ).toHaveAttribute("aria-invalid", "true");
  await expect(grid.getByText(/保存状态：就绪；提交次数：0/)).toBeVisible();
  await grid
    .getByRole("checkbox", { name: /^选择行 / })
    .last()
    .check();
  await grid.getByRole("button", { name: "删除选中行", exact: true }).click();
  await expect(
    grid.getByText("新增：0；修改：0；删除：0", { exact: true }),
  ).toBeVisible();
  await expect(
    grid.getByText("业务修改：否；未提交输入：否", { exact: true }),
  ).toBeVisible();
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(fixture.calls).toEqual(["/api/auth/login", "/api/auth/me"]);
  expect(fixture.errors).toEqual([]);
});

test("既有行删除再恢复保留无效缓冲，明确删除成功只接纳原行", async ({
  page,
}) => {
  const fixture = await openGrid(page);
  const grid = activeGrid(page);
  await grid.getByRole("textbox", { name: "行数量", exact: true }).fill("-");
  await grid
    .getByRole("checkbox", { name: "选择行 row-1", exact: true })
    .check();
  await grid.getByRole("button", { name: "删除选中行", exact: true }).click();
  await expect(
    grid.getByText("新增：0；修改：0；删除：1", { exact: true }),
  ).toBeVisible();
  await grid.getByRole("button", { name: "恢复删除行", exact: true }).click();
  await expect(
    grid.getByRole("textbox", { name: "行数量", exact: true }),
  ).toHaveValue("-");
  await expect(
    grid.getByText("业务修改：否；未提交输入：是", { exact: true }),
  ).toBeVisible();
  await grid
    .getByRole("checkbox", { name: "选择行 row-1", exact: true })
    .check();
  await grid.getByRole("button", { name: "删除选中行", exact: true }).click();
  await grid.getByRole("button", { name: "保存", exact: true }).click();
  await expect(grid.getByText(/保存状态：就绪；提交次数：1/)).toBeVisible();
  await expect(
    grid.getByText("业务修改：否；未提交输入：否", { exact: true }),
  ).toBeVisible();
  await expect(
    grid.getByRole("checkbox", { name: "选择行 row-1", exact: true }),
  ).toHaveCount(0);
  await expect(
    grid.getByRole("checkbox", { name: "选择行 row-2", exact: true }),
  ).toHaveCount(1);
  await expect(
    grid.getByRole("button", { name: "恢复删除行", exact: true }),
  ).toBeDisabled();
  expect(fixture.calls).toEqual(["/api/auth/login", "/api/auth/me"]);
  expect(fixture.errors).toEqual([]);
});

test("新增冻结内容成功接纳新身份，后续删除被归为既有行删除", async ({
  page,
}) => {
  const fixture = await openGrid(page);
  const grid = activeGrid(page);
  await grid.getByRole("button", { name: "新增行", exact: true }).click();
  await grid
    .getByRole("textbox", { name: "第3行名称", exact: true })
    .fill("新建的本地草稿");
  await grid.getByRole("textbox", { name: "第3行数量", exact: true }).fill("8");
  await grid.getByRole("button", { name: "保存", exact: true }).click();
  await expect(grid.getByText(/保存状态：就绪；提交次数：1/)).toBeVisible();
  await expect(
    grid.getByText("新增：0；修改：0；删除：0", { exact: true }),
  ).toBeVisible();
  await expect(
    grid.getByRole("textbox", { name: "第3行名称", exact: true }),
  ).toHaveValue("新建的本地草稿");
  await grid
    .getByRole("checkbox", { name: /^选择行 / })
    .last()
    .check();
  await grid.getByRole("button", { name: "删除选中行", exact: true }).click();
  await expect(
    grid.getByText("新增：0；修改：0；删除：1", { exact: true }),
  ).toBeVisible();
  await grid.getByRole("button", { name: "保存", exact: true }).click();
  await expect(grid.getByText(/保存状态：就绪；提交次数：2/)).toBeVisible();
  await expect(
    grid.getByText("新增：0；修改：0；删除：0", { exact: true }),
  ).toBeVisible();
  expect(fixture.calls).toEqual(["/api/auth/login", "/api/auth/me"]);
  expect(fixture.errors).toEqual([]);
});

test("批量关闭遇到隐藏未知页时定位原恢复出口，不自动人工终止", async ({
  page,
}) => {
  const fixture = await openGrid(page);
  const grid = activeGrid(page);
  await grid
    .getByRole("textbox", { name: "行名称", exact: true })
    .fill("仍需核实");
  await grid.getByRole("combobox", { name: "保存场景" }).click();
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText("响应丢失", { exact: true })
    .click();
  await grid.getByRole("button", { name: "保存", exact: true }).click();
  await expect(grid.getByText(/保存状态：结果未知/)).toBeVisible();
  await page.getByRole("menuitem", { name: /首页/ }).click();
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  await expect(
    activeGrid(page).getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toBeInViewport({ ratio: 1 });
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(fixture.calls).toEqual(["/api/auth/login", "/api/auth/me"]);
  expect(fixture.errors).toEqual([]);
});
