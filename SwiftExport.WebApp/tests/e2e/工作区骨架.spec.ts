import { expect, test } from "@playwright/test";
import type { Locator, Page } from "@playwright/test";
import { readFile } from "node:fs/promises";

type MockIdentity = {
  userId: string;
  userName: string;
  nickName: string;
  permissions: string[];
};
type AuthFixture = {
  identity: MockIdentity;
  meStatus: number;
  apiCalls: string[];
  errors: string[];
  meGate?: Promise<void>;
};
const editablePermissions = ["Products.Product.View", "Products.Product.Edit"];

/** 每个场景只模拟两个实际认证端点，其他 API 请求都视为意外业务访问。 */
async function installAuth(page: Page): Promise<AuthFixture> {
  const fixture: AuthFixture = {
    identity: {
      userId: "user-a",
      userName: "tester",
      nickName: "验收用户",
      permissions: [...editablePermissions],
    },
    meStatus: 200,
    apiCalls: [],
    errors: [],
  };
  page.on("pageerror", (error) => fixture.errors.push(error.message));
  page.on("console", (entry) => {
    if (
      entry.type() === "error" &&
      /hydration|hydrated|react error|maximum update depth|uncaught/i.test(
        entry.text(),
      )
    )
      fixture.errors.push(entry.text());
  });
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (url.pathname.startsWith("/api/"))
      fixture.apiCalls.push(`${request.method()} ${url.pathname}`);
  });
  await page.route("**/api/auth/login", async (route) => {
    expect(route.request().method()).toBe("POST");
    expect(route.request().postDataJSON()).toEqual({
      userName: "tester",
      password: "test-password-only",
    });
    expect(route.request().headers()["authorization"]).toBeUndefined();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ access_token: "test-token-only" }),
    });
  });
  await page.route("**/api/auth/me", async (route) => {
    await fixture.meGate;
    expect(route.request().method()).toBe("GET");
    expect(route.request().headers()["authorization"]).toBe(
      "Bearer test-token-only",
    );
    await route.fulfill({
      status: fixture.meStatus,
      contentType: "application/json",
      body: JSON.stringify(
        fixture.meStatus === 200
          ? fixture.identity
          : { code: "UNAUTHORIZED", message: "测试认证失效" },
      ),
    });
  });
  return fixture;
}

/** 通过真实 Ant Form 登录，重认证时定位覆盖层内的同一表单。 */
async function login(page: Page, reauthenticate = false) {
  const scope = reauthenticate
    ? page.getByRole("dialog")
    : page.locator("main");
  await scope.getByLabel("用户名", { exact: true }).fill("tester");
  await scope.getByLabel("密码", { exact: true }).fill("test-password-only");
  await scope.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  if (reauthenticate) await expect(page.getByRole("dialog")).toHaveCount(0);
}

/** 当前展示只读真实可见 Activity 子树，不把隐藏 DOM 当成活动页面。 */
function activePane(page: Page): Locator {
  return page.locator('[data-testid^="verification-"]:visible');
}

/** 菜单实际打开指定业务身份；重复打开仍走公共 Workspace 入口。 */
async function openProbe(page: Page, kind: "form" | "grid") {
  const menuItem = page.getByRole("menuitem", {
    name: kind === "form" ? "表单验收" : "列表验收",
    exact: true,
  });
  if (!(await menuItem.isVisible()))
    await page.getByRole("menuitem", { name: /骨架验收/ }).click();
  await menuItem.click();
  await expect(activePane(page)).toBeVisible();
}

async function selectScenario(page: Page, label: string) {
  await activePane(page).getByRole("combobox", { name: "保存场景" }).click();
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText(label, { exact: true })
    .click();
}

async function closeActive(page: Page) {
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
}
async function cancelDialog(page: Page, title?: string) {
  await page
    .getByRole("dialog", title ? { name: title, exact: true } : undefined)
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
}
async function save(page: Page) {
  await activePane(page)
    .getByRole("button", { name: "保存", exact: true })
    .click();
}
async function expectUnknown(page: Page) {
  await expect(
    activePane(page).getByText(/保存状态：结果未知；提交次数：1/),
  ).toBeVisible();
}

test.describe("真实浏览器骨架与模拟契约", () => {
  let auth: AuthFixture;
  test.beforeEach(async ({ page }) => {
    auth = await installAuth(page);
    await page.goto("/login");
    await login(page);
  });
  test.afterEach(async () => {
    expect(
      auth.apiCalls.filter(
        (call) => !["POST /api/auth/login", "GET /api/auth/me"].includes(call),
      ),
      "骨架验收不得请求真实业务端点",
    ).toEqual([]);
    expect(auth.errors, "不允许页面异常或 hydration 错误").toEqual([]);
  });

  test("首改未失焦关闭当前及全部，恢复或取消缓冲后可直接关闭", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    const input = activePane(page).getByRole("textbox", {
      name: "行名称",
      exact: true,
    });
    await input.fill("尚未提交名称");
    await expect(input).toBeFocused();
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：是"),
    ).toBeVisible();
    await closeActive(page);
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
    await expect(input).toHaveValue("尚未提交名称");
    await input.fill("初始名称");
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
    await input.fill("再次编辑");
    await input.press("Escape");
    await expect(input).toHaveValue("初始名称");
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
    await closeActive(page);
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(0);
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });

  test("切换清理展示副作用但保留隐藏缓冲，关闭其他仍保护原页", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    const pane = activePane(page);
    const prior = (await pane.getByText(/展示副作用建立/).innerText()).match(
      /展示副作用建立：(\d+)；展示副作用清理：(\d+)/,
    )!;
    await pane.getByRole("textbox", { name: "行数量", exact: true }).fill("1e");
    await openProbe(page, "form");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.getByRole("button", { name: "关闭其他", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText(
      "列表验收有未保存输入",
    );
    await cancelDialog(page);
    await page.getByRole("tab", { name: /列表验收/ }).click();
    await expect(
      activePane(page).getByRole("textbox", { name: "行数量", exact: true }),
    ).toHaveValue("1e");
    await expect
      .poll(async () => {
        const text = await activePane(page)
          .getByText(/展示副作用建立/)
          .innerText();
        const counts = text.match(
          /展示副作用建立：(\d+)；展示副作用清理：(\d+)/,
        )!;
        return (
          Number(counts[1]) > Number(prior[1]) &&
          Number(counts[2]) > Number(prior[2])
        );
      })
      .toBe(true);
  });

  test("无效未完成输入保存失败保留，确认未发送且锁解除", async ({ page }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行数量", exact: true })
      .fill("1e");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：0/),
    ).toBeVisible();
    await expect(
      activePane(page).getByRole("textbox", { name: "行数量", exact: true }),
    ).toHaveValue("1e");
    await expect(
      activePane(page)
        .getByRole("alert")
        .filter({ hasText: "数量必须是正整数" }),
    ).toBeVisible();
    await expect(
      activePane(page).getByRole("button", { name: "保存", exact: true }),
    ).toBeEnabled();
    await closeActive(page);
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
  });

  test("隐藏 Ant Form 先激活校验保存，完整成功后才关闭目标页", async ({
    page,
  }) => {
    await openProbe(page, "form");
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("隐藏表单草稿");
    await openProbe(page, "grid");
    await page.getByRole("button", { name: "关闭其他", exact: true }).click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "保存并关闭", exact: true })
      .click();
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(0);
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await expect(
      activePane(page).getByText(/提交次数：0；回执版本：1/),
    ).toBeVisible();
  });

  test("A 保存期间只读仍可切 B 编辑，迟到完整回执仅接纳 A", async ({
    page,
  }) => {
    await openProbe(page, "form");
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("A 的冻结值");
    await selectScenario(page, "慢成功");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：保存中；提交次数：1/),
    ).toBeVisible();
    await expect(
      activePane(page).getByLabel("名称", { exact: true }),
    ).toBeDisabled();
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(page.getByText("该页正在保存，请等待结果。")).toBeVisible();
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("B 的新输入");
    await expect
      .poll(async () => page.getByRole("tab", { name: /表单验收/ }).innerText())
      .not.toContain("保存中");
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("B 的新输入");
    await expect(
      activePane(page).getByText(/提交次数：0；回执版本：1/),
    ).toBeVisible();
    await page.getByRole("tab", { name: /表单验收/ }).click();
    await expect(
      activePane(page).getByLabel("名称", { exact: true }),
    ).toHaveValue("A 的冻结值");
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：1；回执版本：2/),
    ).toBeVisible();
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
  });

  test("B 明确未生效保留草稿解锁，用户修正后可再次提交", async ({ page }) => {
    await openProbe(page, "form");
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("未生效草稿");
    await selectScenario(page, "确认未生效");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：1；回执版本：1/),
    ).toBeVisible();
    await expect(
      activePane(page).getByLabel("名称", { exact: true }),
    ).toHaveValue("未生效草稿");
    await expect(
      activePane(page).getByText("业务修改：是；未提交输入：否"),
    ).toBeVisible();
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("修正后的草稿");
    await selectScenario(page, "完整成功");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：2；回执版本：2/),
    ).toBeVisible();
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
  });

  for (const scenario of ["业务失败无法确认", "响应丢失"]) {
    test(`C ${scenario}保持冻结锁，不自动重试或普通放弃`, async ({ page }) => {
      await openProbe(page, "grid");
      await activePane(page)
        .getByRole("textbox", { name: "行名称", exact: true })
        .fill("待核实值");
      await selectScenario(page, scenario);
      await save(page);
      await expectUnknown(page);
      await expect(
        activePane(page).getByRole("button", { name: "保存", exact: true }),
      ).toBeDisabled();
      await expect(
        activePane(page).getByRole("button", { name: "放弃修改", exact: true }),
      ).toBeDisabled();
      await expect(
        activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
      ).toHaveAttribute("readonly", "");
      await closeActive(page);
      await expect(
        page.getByText("结果未知，请在原页核实或处理恢复记录。"),
      ).toBeVisible();
      await page.getByRole("button", { name: "关闭全部", exact: true }).click();
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(1);
      await openProbe(page, "form");
      await page.getByRole("tab", { name: /列表验收/ }).click();
      await expectUnknown(page);
      await expect(
        activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
      ).toHaveValue("待核实值");
    });
  }

  test("完整行级部分回执只接纳成功行，失败行继续保护", async ({ page }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("第一行成功");
    await activePane(page)
      .getByRole("textbox", { name: "第2行名称", exact: true })
      .fill("第二行失败草稿");
    await selectScenario(page, "行级部分成功");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：1/),
    ).toBeVisible();
    await expect(
      activePane(page).getByText("业务修改：是；未提交输入：否"),
    ).toBeVisible();
    await expect(
      activePane(page).getByText(
        "行级回执已核对，仅接纳成功行；明确失败行草稿保留。",
      ),
    ).toBeVisible();
    await activePane(page)
      .getByRole("button", { name: "放弃修改", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "放弃", exact: true })
      .click();
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("第一行成功");
    await expect(
      activePane(page).getByRole("textbox", { name: "第2行名称", exact: true }),
    ).toHaveValue("初始名称");
    await expect(
      activePane(page).getByText("业务修改：否；未提交输入：否"),
    ).toBeVisible();
  });

  test("刷新数据和主动退出都保护未提交输入，取消保留原页", async ({ page }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("刷新前输入");
    await activePane(page)
      .getByRole("button", { name: "刷新数据", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toContainText(
      "替换数据前处理未保存输入",
    );
    await cancelDialog(page);
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("刷新前输入");
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
    await expect(page).toHaveURL(/\/workspace/);
    await activePane(page)
      .getByRole("button", { name: "刷新数据", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "放弃并刷新", exact: true })
      .click();
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("初始名称");
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
  });

  test("SPA 离开复用完整输入保护，取消保留、明确放弃才到404", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("SPA离开待保护");
    await page
      .getByRole("link", { name: "验证离开工作区", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
    await expect(page).toHaveURL(/\/workspace/);
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("SPA离开待保护");
    await page
      .getByRole("link", { name: "验证离开工作区", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "放弃并关闭", exact: true })
      .click();
    await expect(page).toHaveURL(/\/not-found$/);
    await expect(page.getByText("页面不存在", { exact: true })).toBeVisible();
  });

  test("退出保存等待中新增另一页输入，最终复核阻止退出", async ({ page }) => {
    await openProbe(page, "form");
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("退出前A草稿");
    await selectScenario(page, "慢成功");
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "保存并关闭", exact: true })
      .click();
    await expect(activePane(page).getByText(/保存状态：保存中/)).toBeVisible();
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("等待期间B新增输入");
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(0);
    await expect(
      page.getByText("其他页签出现新的未保存输入，请处理后再离开。"),
    ).toBeVisible();
    await expect(page).toHaveURL(/\/workspace/);
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("等待期间B新增输入");
    await page.getByRole("button", { name: "退出登录", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
  });

  test("关闭确认弹窗期间撤读权，旧确认不能放弃或移除受保护页", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("弹窗期间撤权保护");
    let release!: () => void;
    auth.meGate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requestStarted = page.waitForRequest(
      (request) => new URL(request.url()).pathname === "/api/auth/me",
    );
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await requestStarted;
    await closeActive(page);
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    auth.identity.permissions = [];
    release();
    await expect(
      page.getByText("当前没有页面读取权限", { exact: true }),
    ).toBeVisible();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "放弃并关闭", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(1);
    auth.identity.permissions = [...editablePermissions];
    auth.meGate = undefined;
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("弹窗期间撤权保护");
  });

  test("有限 beforeunload 仅报告保护，恢复原值后解除", async ({ page }) => {
    await openProbe(page, "grid");
    const check = () =>
      page.evaluate(() => {
        const event = new Event("beforeunload", { cancelable: true });
        window.dispatchEvent(event);
        return event.defaultPrevented;
      });
    expect(await check()).toBe(false);
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("离开提醒输入");
    expect(await check()).toBe(true);
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("初始名称");
    expect(await check()).toBe(false);
  });

  test("URL 定位、返回前进和菜单去重，新草稿具有独立 tabId", async ({
    page,
  }) => {
    await openProbe(page, "form");
    await expect(page).toHaveURL(/view=verification-form&id=form-a/);
    const original = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    await openProbe(page, "grid");
    await page.goBack();
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await expect(activePane(page).getByText(/标签身份/)).toHaveText(original);
    await page.goForward();
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    await openProbe(page, "form");
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(1);
    await expect(activePane(page).getByText(/标签身份/)).toHaveText(original);
    await page.getByRole("button", { name: "新建验收单", exact: true }).click();
    const firstDraft = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    await page.getByRole("button", { name: "新建验收单", exact: true }).click();
    const secondDraft = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    expect(firstDraft).not.toBe(original);
    expect(secondDraft).not.toBe(firstDraft);
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(3);
    await expect(page).toHaveURL(/view=verification-form&draft=/);
    const originalTabId = secondDraft.match(/标签身份：([^；]+)/)![1];
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("新草稿首次保存");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：1；回执版本：2/),
    ).toBeVisible();
    const afterSave = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    expect(afterSave.match(/标签身份：([^；]+)/)![1]).toBe(originalTabId);
    expect(afterSave).not.toBe(secondDraft);
    const savedAddress = new URL(page.url());
    expect(savedAddress.searchParams.has("draft")).toBe(false);
    expect(savedAddress.searchParams.get("id")).toMatch(/^[a-f0-9-]{36}$/);
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("已有Uid的第二次保存");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：2；回执版本：3/),
    ).toBeVisible();
    await expect(activePane(page).getByText(/标签身份/)).toHaveText(afterSave);
    expect(page.url()).toBe(savedAddress.href);
    const savedUid = savedAddress.searchParams.get("id")!;
    await closeActive(page);
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(2);
    // 清除实例后从地址进入，验证新实例不会把 pageKind 前缀误当作服务器 Uid。
    await page.goto(savedAddress.href);
    await expect(page).toHaveURL(/\/login$/);
    await login(page);
    await expect(page).toHaveURL(savedAddress.href);
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("重开已有Uid的未知尝试");
    await selectScenario(page, "响应丢失");
    await save(page);
    await expectUnknown(page);
    const downloadPromise = page.waitForEvent("download");
    await activePane(page)
      .getByRole("button", { name: "导出恢复记录", exact: true })
      .click();
    const downloaded = await downloadPromise;
    const recovery = JSON.parse(
      await readFile((await downloaded.path())!, "utf8"),
    );
    expect(recovery.frozenSubmission[0].Uid).toBe(savedUid);
  });

  test("慢新增期间401仍接纳原会话Uid，同用户恢复按新Uid去重且后续为修改", async ({
    page,
  }) => {
    await page.getByRole("button", { name: "新建验收单", exact: true }).click();
    const draftText = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    const tabId = draftText.match(/标签身份：([^；]+)/)![1];
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("401期间慢新增");
    await selectScenario(page, "慢成功");
    await save(page);
    await expect(activePane(page).getByText(/保存状态：保存中/)).toBeVisible();
    auth.meStatus = 401;
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("重新认证");
    await expect(activePane(page)).toHaveCount(0);
    await expect
      .poll(() => new URL(page.url()).searchParams.get("id"))
      .toMatch(/^[a-f0-9-]{36}$/);
    const savedAddress = new URL(page.url());
    expect(savedAddress.searchParams.has("draft")).toBe(false);
    auth.meStatus = 200;
    await login(page, true);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：1；回执版本：2/),
    ).toBeVisible();
    expect(
      (
        await activePane(page)
          .getByText(/标签身份/)
          .innerText()
      ).match(/标签身份：([^；]+)/)![1],
    ).toBe(tabId);
    await openProbe(page, "grid");
    await page.goBack();
    await expect(page).toHaveURL(savedAddress.href);
    await expect(page.getByRole("tab", { name: /表单验收/ })).toHaveCount(1);
    expect(
      (
        await activePane(page)
          .getByText(/标签身份/)
          .innerText()
      ).match(/标签身份：([^；]+)/)![1],
    ).toBe(tabId);
    await activePane(page)
      .getByLabel("名称", { exact: true })
      .fill("已有Uid的后续修改");
    await selectScenario(page, "完整成功");
    await save(page);
    await expect(
      activePane(page).getByText(/保存状态：就绪；提交次数：2；回执版本：3/),
    ).toBeVisible();
    expect(page.url()).toBe(savedAddress.href);
  });

  test("首次直接深链接登录后恢复指定实体，身份参数不携带草稿或凭据", async ({
    page,
  }) => {
    await page.goto("/workspace?view=verification-grid&id=direct-grid");
    await expect(page).toHaveURL(/\/login$/);
    await login(page);
    await expect(page).toHaveURL(
      /\/workspace\?view=verification-grid&id=direct-grid$/,
    );
    await expect(
      activePane(page).getByText(/业务身份：verification-grid:direct-grid/),
    ).toBeVisible();
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("不得进入地址的草稿");
    expect(page.url()).not.toContain("不得进入地址");
    expect(page.url()).not.toMatch(/token|password|Authorization/i);
  });

  test("非法深链接参数与无读权入口不能展示验证数据", async ({ page }) => {
    await page.goto("/workspace?view=verification-grid&id=invalid.value");
    await expect(page).toHaveURL(/\/login$/);
    await login(page);
    await expect(
      page.getByText("页面地址参数无效。", { exact: true }),
    ).toBeVisible();
    await expect(activePane(page)).toHaveCount(0);
    auth.identity.permissions = [];
    await page.goto("/workspace?view=verification-grid&id=direct-grid");
    await expect(page).toHaveURL(/\/login$/);
    await login(page);
    await expect(activePane(page)).toHaveCount(0);
    await expect(page.getByRole("menuitem", { name: /骨架验收/ })).toHaveCount(
      0,
    );
  });

  test("Ant message、notification、Modal 均接入上下文", async ({ page }) => {
    await page
      .getByRole("button", { name: "验证 message", exact: true })
      .click();
    await expect(page.getByText("消息提示可用")).toBeVisible();
    await page
      .getByRole("button", { name: "验证 notification", exact: true })
      .click();
    await expect(page.getByText("通知提示", { exact: true })).toBeVisible();
    await expect(page.getByText("主题与中文上下文已接入。")).toBeVisible();
    await page.getByRole("button", { name: "验证 Modal", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText(
      "应用弹窗上下文已接入。",
    );
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "知道了", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });

  test("401 遮蔽原内容，同用户重认证恢复原草稿及输入保护", async ({ page }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("重认证保留值");
    const identity = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    auth.meStatus = 401;
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("重新认证");
    await expect(activePane(page)).toHaveCount(0);
    auth.meStatus = 200;
    await login(page, true);
    await expect(activePane(page).getByText(/标签身份/)).toHaveText(identity);
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("重认证保留值");
    await closeActive(page);
    await expect(page.getByRole("dialog")).toContainText("有未保存输入");
    await cancelDialog(page);
  });

  test("401 后不同用户隔离旧草稿，原地址重新定位干净编辑会话", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("旧用户私有草稿");
    const identity = await activePane(page)
      .getByText(/标签身份/)
      .innerText();
    auth.meStatus = 401;
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("重新认证");
    auth.meStatus = 200;
    auth.identity = {
      ...auth.identity,
      userId: "user-b",
      nickName: "第二位用户",
    };
    await login(page, true);
    await expect(page.getByText("第二位用户", { exact: true })).toBeVisible();
    await openProbe(page, "grid");
    await expect(activePane(page).getByText(/标签身份/)).not.toHaveText(
      identity,
    );
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("初始名称");
    await expect(page.getByText("旧用户私有草稿", { exact: true })).toHaveCount(
      0,
    );
  });

  test("已打开页面撤写变只读、撤读隐藏，恢复权限仍保留缓冲", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("撤权待处理");
    auth.identity.permissions = ["Products.Product.View"];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(
      activePane(page).getByRole("button", { name: "保存", exact: true }),
    ).toBeDisabled();
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveAttribute("readonly", "");
    auth.identity.permissions = [];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(
      page.getByText("当前没有页面读取权限", { exact: true }),
    ).toBeVisible();
    await expect(activePane(page)).toHaveCount(0);
    await closeActive(page);
    await expect(
      page.getByText("当前无权处理本页输入，请通过现有合法业务渠道核实。"),
    ).toBeVisible();
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(1);
    auth.identity.permissions = [...editablePermissions];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(
      activePane(page).getByRole("textbox", { name: "行名称", exact: true }),
    ).toHaveValue("撤权待处理");
    auth.identity.permissions = ["Products.Product.View"];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(
      activePane(page).getByRole("button", { name: "保存", exact: true }),
    ).toBeDisabled();
    await closeActive(page);
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "放弃并关闭", exact: true })
      .click();
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(0);
  });

  test("恢复导出失败及未保全不能终止；批量关闭不会启动人工出口", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("导出失败草稿");
    await selectScenario(page, "响应丢失");
    await save(page);
    await expectUnknown(page);
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await expect(
      activePane(page).getByText(/尚未完成合法恢复记录导出/),
    ).toBeVisible();
    await page.evaluate(() => {
      URL.createObjectURL = () => {
        throw new Error("测试下载资源创建失败");
      };
    });
    await activePane(page)
      .getByRole("button", { name: "导出恢复记录", exact: true })
      .click();
    await expect(
      activePane(page).getByText(
        "恢复记录导出失败，未完成保全，继续保留恢复限制。 ",
      ),
    ).toBeVisible();
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expectUnknown(page);
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });

  test("恢复导出白名单材料、取消或未确认保持未知，两步确认后人工关闭", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("必要的冻结值");
    await selectScenario(page, "业务失败无法确认");
    await save(page);
    await expectUnknown(page);
    const downloadPromise = page.waitForEvent("download");
    await activePane(page)
      .getByRole("button", { name: "导出恢复记录", exact: true })
      .click();
    const download = await downloadPromise;
    const path = await download.path();
    expect(path).not.toBeNull();
    const content = await readFile(path!, "utf8");
    const record = JSON.parse(content);
    expect(download.suggestedFilename()).toBe(
      `SwiftExport-恢复记录-${record.recordId}.json`,
    );
    expect(record).toMatchObject({
      businessType: "workspace-verification",
      interfaceOperation: "local.simulateSave (no HTTP request)",
      response: { kind: "unknown", code: "SIMULATION_BUSINESS_UNKNOWN" },
    });
    expect(record.recordId).toMatch(/^[a-f0-9-]{36}$/);
    expect(record.attemptId).toMatch(/^[a-f0-9-]{36}$/);
    expect(Number.isNaN(Date.parse(record.submittedAt))).toBe(false);
    expect(record.frozenSubmission[0]).toMatchObject({
      ClientKey: "row-1",
      Uid: "verification-grid:grid-a:1",
      Version: 1,
      name: "必要的冻结值",
      quantity: 1,
    });
    expect(record.pendingQuestions).toHaveLength(2);
    expect(record.CommandId).toBeUndefined();
    expect(content).not.toMatch(
      /test-token-only|test-password-only|authorization|access_token|"password"|"token"/i,
    );
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toContainText(record.recordId);
    await cancelDialog(page);
    await expectUnknown(page);
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "已妥善保留", exact: true })
      .click();
    await expect(
      page.getByRole("dialog", { name: "确认终止本地恢复", exact: true }),
    ).toContainText("服务器结果仍未确认");
    await cancelDialog(page, "确认终止本地恢复");
    await expectUnknown(page);
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "已妥善保留", exact: true })
      .click();
    await page
      .getByRole("dialog", { name: "确认终止本地恢复", exact: true })
      .getByRole("button", { name: "终止并关闭", exact: true })
      .click();
    await expect(page.getByRole("tab", { name: /列表验收/ })).toHaveCount(0);
  });

  test("恢复记录下载取消且用户未确认保持未知，撤权后禁止再次导出", async ({
    page,
  }) => {
    await openProbe(page, "grid");
    await activePane(page)
      .getByRole("textbox", { name: "行名称", exact: true })
      .fill("下载取消草稿");
    await selectScenario(page, "响应丢失");
    await save(page);
    await expectUnknown(page);
    // 验收浏览器拒绝此次下载，核对实际取消事实；程序仍不能证明用户已保存文件。
    const browserSession = await page.context().newCDPSession(page);
    const { targetInfo } = await browserSession.send("Target.getTargetInfo");
    await browserSession.send("Browser.setDownloadBehavior", {
      behavior: "deny",
      browserContextId: targetInfo.browserContextId,
      eventsEnabled: true,
    });
    const downloadPromise = page.waitForEvent("download");
    await activePane(page)
      .getByRole("button", { name: "导出恢复记录", exact: true })
      .click();
    const cancelledDownload = await downloadPromise;
    expect(await cancelledDownload.failure()).toBe("canceled");
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toContainText("确认记录已妥善保留");
    await cancelDialog(page);
    await expectUnknown(page);
    auth.identity.permissions = ["Products.Product.View"];
    await page.getByRole("button", { name: "刷新权限", exact: true }).click();
    await expect(activePane(page).getByText(/当前没有编辑权限/)).toBeVisible();
    const unexpectedDownloads: string[] = [];
    page.on("download", (download) =>
      unexpectedDownloads.push(download.suggestedFilename()),
    );
    await activePane(page)
      .getByRole("button", { name: "导出恢复记录", exact: true })
      .click();
    await expect(
      activePane(page).getByText(/当前身份或业务权限不允许保全/),
    ).toBeVisible();
    await activePane(page)
      .getByRole("button", { name: "终止本地恢复", exact: true })
      .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(unexpectedDownloads).toEqual([]);
    await expectUnknown(page);
  });
});
