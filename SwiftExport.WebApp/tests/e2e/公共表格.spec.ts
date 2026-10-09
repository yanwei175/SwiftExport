import {
  installBrowserSessionProtocol,
  mockJwtToken,
  authenticationCalls,
} from "./会话模拟";
import { expect, test } from "@playwright/test";
const authToken = mockJwtToken("tester");
import type { Locator, Page } from "@playwright/test";
import { readFile } from "node:fs/promises";

/** 浏览器证据只针对真实公共 UI，不允许夹具或验收页发送业务写入。 */
interface Observations {
  apiCalls: string[];
  errors: string[];
}
interface AuthFixture extends Observations {
  permissions: string[];
}

/** 收集页面与 hydration 错误，并在发现未声明 API 时立即阻断真实访问。 */
async function observePage(page: Page): Promise<Observations> {
  const observed: Observations = { apiCalls: [], errors: [] };
  page.on("pageerror", (error) => observed.errors.push(error.message));
  page.on("console", (entry) => {
    if (
      entry.type() === "error" &&
      /hydration|hydrated|react error|maximum update depth|uncaught/i.test(
        entry.text(),
      )
    )
      observed.errors.push(entry.text());
  });
  page.on("request", (request) => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith("/api/"))
      observed.apiCalls.push(`${request.method()} ${path}`);
  });
  // 只拦真实后端路径；Vite 的 /app/shared/api 源模块必须正常加载。
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    (route) => route.abort("blockedbyclient"),
  );
  return observed;
}

/** 登录和 me 使用真实传输字段，只有精确的两个认证端点允许模拟响应。 */
async function installAuth(page: Page): Promise<AuthFixture> {
  const fixture: AuthFixture = {
    ...(await observePage(page)),
    permissions: ["Products.Product.View", "Products.Product.Edit"],
  };
  await page.route("**/api/auth/login", async (route) => {
    expect(route.request().method()).toBe("POST");
    expect(route.request().postDataJSON()).toEqual({
      userName: "tester",
      password: "grid-test-password",
    });
    expect(route.request().headers()["authorization"]).toBeUndefined();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ access_token: authToken }),
    });
  });
  await page.route("**/api/auth/me", async (route) => {
    expect(route.request().method()).toBe("GET");
    expect(route.request().headers()["authorization"]).toBe(
      "Bearer " + authToken,
    );
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        userId: "grid-test-user",
        userName: "tester",
        nickName: "表格验收用户",
        permissions: fixture.permissions,
      }),
    });
  });
  return fixture;
}

/** 只使用真实可见页签，隐藏 Activity 的输入不能冒充活动编辑器。 */
function activePane(page: Page): Locator {
  return page.locator('[data-testid^="verification-"]:visible');
}
function dataGrid(page: Page): Locator {
  return page.getByRole("group", { name: "数据表格", exact: true });
}

/** 通过公共菜单打开已有验证 feature；页面依旧拥有原始缓冲与关闭保护。 */
async function openProbe(page: Page, kind: "form" | "grid") {
  const item = page.getByRole("menuitem", {
    name: kind === "grid" ? "列表验收" : "表单验收",
    exact: true,
  });
  if (!(await item.isVisible()))
    await page.getByRole("menuitem", { name: /骨架验收/ }).click();
  await item.click();
  await expect(activePane(page)).toBeVisible();
}
async function cancelClose(page: Page) {
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
}

/** 使用可取消的真实 DOM 事件观察是否拦截，不打开原生 Chrome 收藏弹窗。 */
async function dispatchCtrlD(target: Locator): Promise<boolean> {
  return target.evaluate((element) => {
    const event = new KeyboardEvent("keydown", {
      key: "d",
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    element.dispatchEvent(event);
    return event.defaultPrevented;
  });
}

/** 测试独立 HTML 只加载真实 Vite 模块，不注册业务页面，也不发布夹具。 */
async function openGridFixture(page: Page): Promise<void> {
  await page.route(
    (url) =>
      decodeURIComponent(url.pathname) === "/tests/e2e/fixtures/公共表格.html",
    async (route) =>
      route.fulfill({
        contentType: "text/html",
        body: await readFile(
          new URL("./fixtures/公共表格.html", import.meta.url),
          "utf8",
        ),
      }),
  );
  await page.goto("/tests/e2e/fixtures/公共表格.html");
  await expect(dataGrid(page)).toBeVisible();
}

test.describe("真实页复用公共表格的输入保护", () => {
  let auth: AuthFixture;
  test.beforeEach(async ({ page }) => {
    auth = await installAuth(page);
    await installBrowserSessionProtocol(page, authToken);
    await page.goto("/login");
    await page.getByLabel("用户名", { exact: true }).fill("tester");
    await page.getByLabel("密码", { exact: true }).fill("grid-test-password");
    await page.getByRole("button", { name: "登录", exact: true }).click();
    await expect(
      page.getByRole("button", { name: "退出登录", exact: true }),
    ).toBeVisible();
    await openProbe(page, "grid");
    await expect(dataGrid(page)).toBeVisible();
  });
  test.afterEach(() => {
    expect(
      auth.apiCalls.filter((call) => !authenticationCalls.includes(call)),
    ).toEqual([]);
    expect(auth.errors, "公共 Grid 不能造成页面或 hydration 异常").toEqual([]);
  });

  test("首改未失焦关闭当前及全部仍保护，还原与 Escape 只解除对应缓冲", async ({
    page,
  }) => {
    const input = activePane(page).getByRole("textbox", {
      name: "行名称",
      exact: true,
    });
    await input.fill("公共表格未失焦文本");
    await expect(input).toBeFocused();
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：是"),
    ).toBeVisible();
    await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelClose(page);
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelClose(page);
    await expect(input).toHaveValue("公共表格未失焦文本");
    await input.fill("初始名称");
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
    await input.fill("取消文本");
    await input.press("Escape");
    await expect(input).toHaveValue("初始名称");
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
    await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(0);
  });

  test("Activity 隐藏不丢原始输入，关闭其他仍保护原 Grid", async ({ page }) => {
    await activePane(page)
      .getByRole("textbox", { name: "行数量", exact: true })
      .fill("1e");
    await openProbe(page, "form");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.getByRole("button", { name: "关闭其他", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText(
      "列表验收有未保存输入",
    );
    await cancelClose(page);
    await page.getByRole("tab", { name: /列表验收/ }).click();
    const input = activePane(page).getByRole("textbox", {
      name: "行数量",
      exact: true,
    });
    await expect(input).toHaveValue("1e");
    await activePane(page)
      .getByRole("button", { name: "保存", exact: true })
      .click();
    await expect(input).toHaveAttribute("aria-invalid", "true");
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：0/),
    ).toBeVisible();
    const errorId = await input.getAttribute("aria-describedby");
    expect(errorId).toBeTruthy();
    await expect(page.locator(`[id="${errorId}"]`)).toContainText(
      "数量必须是正整数",
    );
    await input.press("Escape");
    await expect(input).toHaveValue("1");
    await expect(input).toHaveAttribute("aria-invalid", "false");
  });

  test("指定活动列填充只转交原始文本，另一列与两个基线均不被改写", async ({
    page,
  }) => {
    const pane = activePane(page);
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 row-1", exact: true })
      .check();
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 row-2", exact: true })
      .check();
    await pane.getByRole("textbox", { name: "行数量", exact: true }).fill("1e");
    await pane
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("只填名称");
    await dataGrid(page)
      .getByRole("button", { name: "向下填充", exact: true })
      .click();
    await expect(
      pane.getByRole("textbox", { name: "第2行名称", exact: true }),
    ).toHaveValue("只填名称");
    await expect(
      pane.getByRole("textbox", { name: "第2行数量", exact: true }),
    ).toHaveValue("1");
    await expect(
      pane.getByRole("textbox", { name: "行数量", exact: true }),
    ).toHaveValue("1e");
    await expect(pane.getByText("业务修改：否；未提交输入：是")).toBeVisible();
    await pane.getByRole("textbox", { name: "行数量", exact: true }).fill("9");
    const quantityCell = dataGrid(page).locator(
      '[data-row-key="row-1"] [data-column-key="quantity"]',
    );
    await quantityCell.focus();
    await quantityCell.press("Control+d");
    await expect(
      pane.getByRole("textbox", { name: "第2行数量", exact: true }),
    ).toHaveValue("9");
    await expect(
      pane.getByRole("textbox", { name: "第2行名称", exact: true }),
    ).toHaveValue("只填名称");
    for (const label of ["行名称", "第2行名称", "行数量", "第2行数量"]) {
      await pane
        .getByRole("textbox", { name: label, exact: true })
        .press("Escape");
    }
    await expect(pane.getByText("业务修改：否；未提交输入：否")).toBeVisible();
  });

  test("输入框 Ctrl+D 不抢，撤编辑权限后公共表格保持输入且禁止填充", async ({
    page,
  }) => {
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择当前页全部行", exact: true })
      .check();
    const input = activePane(page).getByRole("textbox", {
      name: "行名称",
      exact: true,
    });
    await input.fill("输入内快捷键");
    expect(await dispatchCtrlD(input)).toBe(false);
    await expect(
      activePane(page).getByRole("textbox", { name: "第2行名称", exact: true }),
    ).toHaveValue("初始名称");
    auth.permissions = ["Products.Product.View"];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(input).toHaveJSProperty("readOnly", true);
    await expect(input).toHaveValue("输入内快捷键");
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeDisabled();
    const cell = dataGrid(page).locator(
      '[data-row-key="row-1"] [data-column-key="name"]',
    );
    await cell.focus();
    expect(await dispatchCtrlD(cell)).toBe(false);
    await expect(
      activePane(page).getByRole("textbox", { name: "第2行名称", exact: true }),
    ).toHaveValue("初始名称");
  });
});

test.describe("公共表格受控 UI 夹具", () => {
  let observed: Observations;
  test.beforeEach(async ({ page }) => {
    // 加载前即持有观测记录，夹具启动失败时也保留 API/页面错误断言。
    observed = await observePage(page);
    await openGridFixture(page);
  });
  test.afterEach(() => {
    expect(observed.apiCalls, "公共 Grid 不得发送任何 API").toEqual([]);
    expect(observed.errors).toEqual([]);
  });

  test("可见顺序决定源，跨页选择不参与，逐行只读目标被跳过", async ({
    page,
  }) => {
    await page
      .getByRole("button", { name: "使用乱序及跨页选择", exact: true })
      .click();
    for (const key of ["a", "b", "c", "d"]) {
      await expect(
        dataGrid(page).getByRole("checkbox", {
          name: `选择行 ${key}`,
          exact: true,
        }),
      ).toBeChecked();
    }
    await page
      .getByRole("textbox", { name: "b-name", exact: true })
      .fill("受控源文本");
    await dataGrid(page)
      .getByRole("button", { name: "向下填充", exact: true })
      .click();
    await expect(page.getByTestId("fill-history")).toHaveText(
      JSON.stringify([{ columnKey: "name", source: "b", targets: ["c", "d"] }]),
    );
    await expect(
      page.getByRole("textbox", { name: "a-name", exact: true }),
    ).toHaveValue("只读目标");
    await expect(
      page.getByRole("textbox", { name: "a-name", exact: true }),
    ).toHaveJSProperty("readOnly", true);
    await expect(
      page.getByRole("textbox", { name: "c-name", exact: true }),
    ).toHaveValue("受控源文本");
    await expect(
      page.getByRole("textbox", { name: "d-name", exact: true }),
    ).toHaveValue("受控源文本");
    await page.getByRole("button", { name: "清除选择", exact: true }).click();
    await expect(page.getByTestId("selection-value")).toHaveText("[]");
    await expect(
      dataGrid(page).getByRole("checkbox", { name: "选择行 b", exact: true }),
    ).not.toBeChecked();
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeDisabled();
  });

  test("Ctrl+D 只填活动列，输入与只读列不拦截，错误文本关联且 Escape 转交取消", async ({
    page,
  }) => {
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择当前页全部行", exact: true })
      .check();
    const quantity = page.getByRole("textbox", {
      name: "b-quantity",
      exact: true,
    });
    await quantity.fill("1e");
    expect(await dispatchCtrlD(quantity)).toBe(false);
    await expect(page.getByTestId("shortcut-value")).toContainText(
      '"prevented":false',
    );
    await expect(page.getByTestId("fill-history")).toHaveText("[]");
    const quantityCell = dataGrid(page).locator(
      '[data-row-key="b"] [data-column-key="quantity"]',
    );
    await quantityCell.focus();
    await quantityCell.press("Control+d");
    await expect(page.getByTestId("shortcut-value")).toContainText(
      '"prevented":true',
    );
    await expect(
      page.getByRole("textbox", { name: "c-quantity", exact: true }),
    ).toHaveValue("1e");
    await expect(
      page.getByRole("textbox", { name: "c-name", exact: true }),
    ).toHaveValue("可写目标C");
    await expect(quantity).toHaveAttribute("aria-invalid", "true");
    const errorId = await quantity.getAttribute("aria-describedby");
    expect(errorId).toBeTruthy();
    await expect(page.locator(`[id="${errorId}"]`)).toHaveText(
      "数量尚未完成。",
    );
    const readOnlyCell = dataGrid(page).locator(
      '[data-row-key="b"] [data-column-key="immutable"]',
    );
    await readOnlyCell.focus();
    expect(await dispatchCtrlD(readOnlyCell)).toBe(false);
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeDisabled();
    await expect(page.getByTestId("fill-history")).toHaveText(
      JSON.stringify([
        { columnKey: "quantity", source: "b", targets: ["c", "d"] },
      ]),
    );
    await quantity.press("Escape");
    await expect(quantity).toHaveValue("7");
    await expect(quantity).toHaveAttribute("aria-invalid", "false");
    await expect(page.getByTestId("cancel-value")).toHaveText("b:quantity");
    await expect(page.locator(`[id="${errorId}"]`)).toHaveCount(0);
    await expect(page.getByText("自定义-b", { exact: true })).toBeVisible();
  });

  test("全只读或只有只读目标时不产生填充，选择本身不算编辑权限", async ({
    page,
  }) => {
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 b", exact: true })
      .check();
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 a", exact: true })
      .check();
    const cell = dataGrid(page).locator(
      '[data-row-key="b"] [data-column-key="name"]',
    );
    await cell.focus();
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeDisabled();
    expect(await dispatchCtrlD(cell)).toBe(false);
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 c", exact: true })
      .check();
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeEnabled();
    await page.getByRole("checkbox", { name: "表格只读", exact: true }).check();
    await expect(
      page.getByRole("textbox", { name: "b-name", exact: true }),
    ).toHaveJSProperty("readOnly", true);
    await expect(
      dataGrid(page).getByRole("button", { name: "向下填充", exact: true }),
    ).toBeDisabled();
    expect(await dispatchCtrlD(cell)).toBe(false);
    await expect(page.getByTestId("fill-history")).toHaveText("[]");
  });

  test("分页仅发受控意图，行号按页偏移显示且稳定 rowKey 与选择不变化", async ({
    page,
  }) => {
    const row = dataGrid(page).locator('[data-row-key="b"]');
    await expect(row.locator("td").nth(1)).toHaveText("21");
    await dataGrid(page)
      .getByRole("checkbox", { name: "选择行 b", exact: true })
      .check();
    await dataGrid(page).locator('.ant-pagination-item[title="4"]').click();
    await expect(page.getByTestId("page-request")).toHaveText("4");
    await expect(row.locator("td").nth(1)).toHaveText("21");
    await expect(
      dataGrid(page).locator(".ant-pagination-item-active"),
    ).toHaveAttribute("title", "3");
    await page
      .getByRole("button", { name: "接受分页请求", exact: true })
      .click();
    await expect(row.locator("td").nth(1)).toHaveText("31");
    await expect(
      dataGrid(page).getByRole("checkbox", { name: "选择行 b", exact: true }),
    ).toBeChecked();
    await expect(page.getByTestId("selection-value")).toHaveText('["b"]');
  });
});
