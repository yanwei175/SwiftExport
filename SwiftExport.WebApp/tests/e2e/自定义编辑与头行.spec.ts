import { expect, test } from "@playwright/test";
import type { Locator, Page } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { authenticationCalls, mockBrowserSession } from "./会话模拟";

type SessionFixture = Awaited<ReturnType<typeof mockBrowserSession>>;
type ProbeFixture = {
  session: SessionFixture;
  categoryStatus: number;
  readyCategoryCalls: number;
  categories: { uid: string; categoryName: string }[];
};
const observations = new WeakMap<Page, ProbeFixture>();
const permissions = [
  "Products.Product.View",
  "Products.Product.Edit",
  "Products.Category.View",
];

/** 观测真实编辑会话投影；raw 优先，身份及版本用于验证填充不会复制元数据。 */
interface DocumentLineSnapshot {
  rowKey: string;
  uid: string;
  version: number;
  name: string;
  quantity: number | string;
  kind: string;
  businessDate: string | null;
  categoryId: string | null;
  referenceCode: string;
}

/** 读取验收页白名单投影，不绕过控件直接调用 feature 或修改其内部状态。 */
async function lineValues(pane: Locator): Promise<DocumentLineSnapshot[]> {
  return JSON.parse(
    (await pane.getByTestId("document-line-values").textContent())!,
  );
}

/** 始终定位真实可见 Activity 展示，隐藏节点不能代替活动编辑器。 */
function documentPane(page: Page): Locator {
  return page.locator('[data-testid^="document-verification-"]:visible');
}
function basicPane(page: Page): Locator {
  return page.locator('[data-testid^="verification-"]:visible');
}
/** 新场景复用真实 Cookie 会话模拟，分类只允许精确生成客户端路径，不访问真实后端。 */
async function initialize(page: Page) {
  const session = await mockBrowserSession(page, {
    permissions: [...permissions],
  });
  const fixture: ProbeFixture = {
    session,
    categoryStatus: 200,
    readyCategoryCalls: 0,
    categories: [
      { uid: "category-a", categoryName: "分类甲" },
      { uid: "category-b", categoryName: "分类乙" },
    ],
  };
  observations.set(page, fixture);
  page.on("console", (entry) => {
    if (
      entry.type() === "error" &&
      /hydration|hydrated|react error|maximum update depth|uncaught/i.test(
        entry.text(),
      )
    )
      session.errors.push(entry.text());
  });
  await page.route(
    (url) => url.pathname === "/api/productcategory/all",
    async (route) => {
      expect(route.request().method()).toBe("GET");
      expect(route.request().headers().authorization).toBe(
        "Bearer " + session.token,
      );
      await route.fulfill({
        status: fixture.categoryStatus,
        json:
          fixture.categoryStatus === 200
            ? { success: true, data: fixture.categories }
            : {
                success: false,
                error: {
                  code: "BUSINESS_BLOCKED",
                  message: "模拟分类读取失败",
                },
              },
      });
    },
  );
  await page.goto("/");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page.getByLabel("密码", { exact: true }).fill("head-line-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  const menu = page.getByRole("menuitem", { name: /表单验收/ });
  if (!(await menu.isVisible()))
    await page.getByRole("menuitem", { name: /骨架验收/ }).click();
  await menu.click();
  await basicPane(page)
    .getByRole("button", { name: "打开头行验收", exact: true })
    .click();
  await expect(documentPane(page)).toBeVisible();
  await documentPane(page)
    .getByRole("combobox", { name: "明细1分类", exact: true })
    .click();
  await expect(
    page
      .locator(".ant-select-dropdown:visible")
      .getByText("分类甲", { exact: true }),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  fixture.readyCategoryCalls = session.apiCalls.filter(
    (call) => call === "GET /api/productcategory/all",
  ).length;
  expect(fixture.readyCategoryCalls).toBeGreaterThan(0);
  return fixture;
}
/** 选择真实 Ant 下拉选项，避免隐藏的同名控件污染断言。 */
async function selectOption(page: Page, control: Locator, label: string) {
  await control.click();
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText(label, { exact: true })
    .click();
}
async function scenario(page: Page, label: string) {
  await selectOption(
    page,
    documentPane(page).getByRole("combobox", {
      name: "头行保存场景",
      exact: true,
    }),
    label,
  );
}
/** 单列填充按单元格区域选择，行勾选仍只用于行级操作。 */
async function selectLineRange(pane: Locator, columnKey: string) {
  const cells = pane.locator(`td[data-column-key="${columnKey}"]`);
  await cells.first().click({ position: { x: 2, y: 2 } });
  await cells.nth(1).click({
    modifiers: ["Shift"],
    position: { x: 2, y: 2 },
  });
}
async function cancelDialog(page: Page) {
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
}
/** 页签实例 ID 用于跨 Activity 定位，业务身份和显示标题均不替代 tabId。 */
async function tabForDocument(page: Page): Promise<Locator> {
  const id = (await documentPane(page).getAttribute("data-testid"))!.replace(
    "document-verification-",
    "",
  );
  return page.locator(`.ant-tabs-tab[data-node-key="${id}"]`);
}

test.beforeEach(async ({ page }) => {
  await initialize(page);
});
test.afterEach(async ({ page }) => {
  const fixture = observations.get(page);
  if (!fixture) return;
  expect(fixture.session.errors, "React、hydration 或页面异常必须报告").toEqual(
    [],
  );
  expect(
    fixture.session.apiCalls.filter(
      (call) =>
        !authenticationCalls.includes(call) &&
        call !== "GET /api/productcategory/all",
    ),
    "本地保存验收不能向真实后端写入",
  ).toEqual([]);
});

test("头金额或未失焦明细无效时不派发，取消/还原恢复保护", async ({ page }) => {
  const pane = documentPane(page);
  await pane.getByLabel("单据金额", { exact: true }).fill("1.");
  await pane.getByLabel("明细1数量", { exact: true }).fill("-");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("提交尝试：0");
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveValue("-");
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveAttribute(
    "aria-invalid",
    "true",
  );
  await expect(pane).toContainText("未提交输入：是");
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await cancelDialog(page);
  await pane.getByLabel("单据金额", { exact: true }).fill("");
  await pane.getByLabel("明细1数量", { exact: true }).press("Escape");
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveValue("1");
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(pane).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("隐藏后输入保留，关闭隐藏头行仍保护，保存统一唤回校验", async ({
  page,
}) => {
  const tab = await tabForDocument(page);
  const input = documentPane(page).getByLabel("明细1数量", { exact: true });
  await input.fill("-");
  await page.getByRole("menuitem", { name: /首页/ }).click();
  await expect(documentPane(page)).toHaveCount(0);
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并关闭", exact: true })
    .click();
  await expect(documentPane(page)).toBeVisible();
  await expect(input).toHaveValue("-");
  await expect(documentPane(page)).toContainText("提交尝试：0");
  await expect(documentPane(page)).toContainText("保存状态：idle");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("受控枚举显式填充当前列，日期只接受面板值，Escape取消短期输入", async ({
  page,
}) => {
  const pane = documentPane(page);
  const kind = pane.getByRole("combobox", { name: "明细1类型", exact: true });
  await selectOption(page, kind, "易碎");
  await expect(pane).toContainText("未提交输入：是");
  await selectLineRange(pane, "kind");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect(pane.locator('td[data-column-key="kind"]').nth(1)).toContainText(
    "易碎",
  );
  await expect(pane.getByLabel("明细2数量", { exact: true })).toHaveValue("1");
  const date = pane.getByLabel("明细1日期", { exact: true });
  await expect(date).toHaveAttribute("readonly", "");
  await date.click();
  await page
    .locator(".ant-picker-dropdown:visible")
    .getByText("今天", { exact: true })
    .click();
  await expect(date).not.toHaveValue("");
  await expect(pane.getByLabel("业务日期", { exact: true })).toHaveAttribute(
    "readonly",
    "",
  );
  await pane.getByLabel("明细1名称", { exact: true }).fill("临时名称");
  await pane.getByLabel("明细1名称", { exact: true }).press("Escape");
  await expect(pane.getByLabel("明细1名称", { exact: true })).toHaveValue(
    "验收明细",
  );
});

test("候选搜索与值分离，缺项保留标签但不能填为新候选", async ({ page }) => {
  const pane = documentPane(page);
  const category = pane.getByRole("combobox", {
    name: "明细1分类",
    exact: true,
  });
  await category.click();
  await category.fill("分类甲");
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText("分类甲", { exact: true })
    .click();
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  const fixture = observations.get(page)!;
  fixture.categories = [{ uid: "category-b", categoryName: "分类乙" }];
  await pane.getByRole("button", { name: "刷新分类候选", exact: true }).click();
  await expect
    .poll(
      () =>
        fixture.session.apiCalls.filter(
          (call) => call === "GET /api/productcategory/all",
        ).length,
    )
    .toBe(fixture.readyCategoryCalls + 1);
  await expect(
    pane.getByRole("button", { name: "刷新分类候选", exact: true }),
  ).toBeEnabled();
  await expect(
    pane.locator('td[data-column-key="categoryId"]').first(),
  ).toContainText("分类甲");
  await selectLineRange(pane, "categoryId");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect(pane).toContainText("没有填充其他行");
  await expect(
    pane.locator('td[data-column-key="categoryId"]').nth(1),
  ).not.toContainText("分类甲");
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
});

test("头行多列区域任一源无效时整次拒绝，不先填入合法列", async ({ page }) => {
  const pane = documentPane(page);
  await pane.getByLabel("明细1名称", { exact: true }).fill("只应保留在源行");
  await pane.getByLabel("明细1数量", { exact: true }).fill("-");
  await pane
    .locator('td[data-column-key="name"]')
    .first()
    .click({ position: { x: 2, y: 2 } });
  await pane
    .locator('td[data-column-key="quantity"]')
    .nth(1)
    .click({ modifiers: ["Shift"], position: { x: 2, y: 2 } });
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect(pane.getByLabel("明细2名称", { exact: true })).toHaveValue(
    "验收明细",
  );
  await expect(pane.getByLabel("明细2数量", { exact: true })).toHaveValue("1");
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveValue("-");
  await expect(pane).toContainText("没有填充其他行");
  await expect(pane).toContainText("提交尝试：0");
  await expect(pane).toContainText("业务修改：否；未提交输入：是");
});

test("名称区域填充关联隐藏字段，身份版本及未选字段不变，取消恢复整组输入", async ({
  page,
}) => {
  const pane = documentPane(page);
  const before = await lineValues(pane);
  await expect(pane.locator('th[data-column-key="referenceCode"]')).toHaveCount(
    0,
  );
  expect(before.map((line) => line.referenceCode)).toEqual(["REF-A", "REF-B"]);
  await selectLineRange(pane, "name");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  // 显示名称相同也必须填充关联编号；只改变该组的目标值，不能复制身份、版本或数量。
  await expect
    .poll(() => lineValues(pane))
    .toEqual([before[0], { ...before[1], referenceCode: "REF-A" }]);
  await expect(pane).toContainText("业务修改：否；未提交输入：是");
  await pane.getByLabel("明细2名称", { exact: true }).press("Escape");
  await expect.poll(() => lineValues(pane)).toEqual(before);
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await expect(pane).toContainText("提交尝试：0");
});

test("仅关联隐藏字段变化，切页后关闭原标签仍保护，取消后正常释放", async ({
  page,
}) => {
  const pane = documentPane(page);
  const before = await lineValues(pane);
  const tab = await tabForDocument(page);
  await selectLineRange(pane, "name");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect
    .poll(() => lineValues(pane))
    .toEqual([before[0], { ...before[1], referenceCode: "REF-A" }]);
  await page.getByRole("menuitem", { name: /首页/ }).click();
  await expect(documentPane(page)).toHaveCount(0);
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await cancelDialog(page);
  await expect(tab).toBeVisible();
  await tab.getByRole("tab").click();
  await expect(documentPane(page)).toBeVisible();
  await expect
    .poll(() => lineValues(pane))
    .toEqual([before[0], { ...before[1], referenceCode: "REF-A" }]);
  await pane.getByLabel("明细2名称", { exact: true }).press("Escape");
  await expect.poll(() => lineValues(pane)).toEqual(before);
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(tab).toHaveCount(0);
});

test("关联填充读取源名称与隐藏字段的最新raw，未失焦输入不会退回基线", async ({
  page,
}) => {
  const pane = documentPane(page);
  const before = await lineValues(pane);
  await pane
    .getByRole("button", { name: "模拟关联字段修改", exact: true })
    .click();
  const name = pane.getByLabel("明细1名称", { exact: true });
  await name.fill("最新未失焦名称");
  await expect(name).toBeFocused();
  await selectLineRange(pane, "name");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect
    .poll(() => lineValues(pane))
    .toEqual(
      before.map((line) => ({
        ...line,
        name: "最新未失焦名称",
        referenceCode: "REF-RAW-A",
      })),
    );
  await expect(pane).toContainText("业务修改：否；未提交输入：是");
  // 取消目标单元格要撤销它的关联输入，源行独立的输入保护仍然存在。
  await pane.getByLabel("明细2名称", { exact: true }).press("Escape");
  await expect
    .poll(() => lineValues(pane))
    .toEqual([
      {
        ...before[0],
        name: "最新未失焦名称",
        referenceCode: "REF-RAW-A",
      },
      before[1],
    ]);
  await expect(pane).toContainText("未提交输入：是");
  await name.press("Escape");
  await expect.poll(() => lineValues(pane)).toEqual(before);
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
});

test("隐藏源字段无效时整块区域拒绝，无合法列部分填入且保存不派发", async ({
  page,
}) => {
  const pane = documentPane(page);
  await pane.getByLabel("明细1名称", { exact: true }).fill("不能先填此名称");
  await pane.getByLabel("明细1数量", { exact: true }).fill("9");
  await pane
    .getByRole("button", { name: "模拟关联字段无效", exact: true })
    .click();
  const before = await lineValues(pane);
  expect(before[0]?.referenceCode).toBe("");
  await pane
    .locator('td[data-column-key="name"]')
    .first()
    .click({ position: { x: 2, y: 2 } });
  await pane
    .locator('td[data-column-key="quantity"]')
    .nth(1)
    .click({ modifiers: ["Shift"], position: { x: 2, y: 2 } });
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect(pane).toContainText("没有填充其他行");
  await expect.poll(() => lineValues(pane)).toEqual(before);
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("提交尝试：0");
  await expect(pane).toContainText("保存状态：idle");
  await expect.poll(() => lineValues(pane)).toEqual(before);
  await expect(pane).toContainText("未提交输入：是");
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await cancelDialog(page);
});

test("关联隐藏字段进入同一次头行冻结与成功接纳，完整回执后全会话clean", async ({
  page,
}) => {
  const pane = documentPane(page);
  const before = await lineValues(pane);
  await pane
    .getByRole("button", { name: "模拟关联字段修改", exact: true })
    .click();
  await pane.getByLabel("明细1名称", { exact: true }).fill("关联保存名称");
  await selectLineRange(pane, "name");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await expect(pane).toContainText("未提交输入：是");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("头与所有明细完整模拟回执已核对接纳");
  await expect
    .poll(() => lineValues(pane))
    .toEqual(
      before.map((line) => ({
        ...line,
        version: line.version + 1,
        name: "关联保存名称",
        referenceCode: "REF-RAW-A",
      })),
    );
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await expect(pane).toContainText("提交尝试：1");
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(pane).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("关联填充结果未知时锁住同一冻结内容，导出包括隐藏字段与原身份版本", async ({
  page,
}) => {
  const pane = documentPane(page);
  const before = await lineValues(pane);
  await pane
    .getByRole("button", { name: "模拟关联字段修改", exact: true })
    .click();
  await pane.getByLabel("明细1名称", { exact: true }).fill("关联冻结名称");
  await selectLineRange(pane, "name");
  await pane.getByRole("button", { name: "向下填充", exact: true }).click();
  await scenario(page, "响应丢失");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("保存状态：unknown");
  await expect(pane.getByLabel("明细2名称", { exact: true })).toHaveAttribute(
    "readonly",
    "",
  );
  await expect(
    pane.getByRole("button", { name: "向下填充", exact: true }),
  ).toBeDisabled();
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const downloading = page.waitForEvent("download");
  await pane.getByRole("button", { name: "导出恢复记录", exact: true }).click();
  const download = await downloading;
  const record = JSON.parse(await readFile((await download.path())!, "utf8"));
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(record.interfaceOperation).toContain("no HTTP request");
  expect(record.frozenSubmission.lines).toEqual(
    before.map(({ rowKey, uid, version, ...values }) => ({
      ...values,
      ClientKey: rowKey,
      Uid: uid,
      Version: version,
      name: "关联冻结名称",
      referenceCode: "REF-RAW-A",
    })),
  );
  expect(JSON.stringify(record)).not.toMatch(
    /Authorization|access_token|password|CommandId|csrf-test/,
  );
  await expect(pane).toContainText("保存状态：unknown");
  await expect(pane).toContainText("提交尝试：1");
});

test("分类撤读权只冻结对应编辑，不扩大权限且不重新读取", async ({ page }) => {
  const fixture = observations.get(page)!;
  fixture.session.identity.permissions = [
    "Products.Product.View",
    "Products.Product.Edit",
  ];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  const pane = documentPane(page);
  await expect(pane).toContainText("无分类读取权限");
  await expect(
    pane.getByRole("combobox", { name: "明细1分类", exact: true }),
  ).toBeDisabled();
  await expect(pane.getByLabel("明细1名称", { exact: true })).toBeEditable();
  expect(
    fixture.session.apiCalls.filter(
      (call) => call === "GET /api/productcategory/all",
    ),
  ).toHaveLength(fixture.readyCategoryCalls);
});

test("分类读取失败保留明确错误，人工重读空结果不产生编辑修改", async ({
  page,
}) => {
  const fixture = observations.get(page)!;
  fixture.categoryStatus = 422;
  const pane = documentPane(page);
  await pane.getByRole("button", { name: "刷新分类候选", exact: true }).click();
  await expect(pane).toContainText("模拟分类读取失败");
  await expect(
    pane.getByRole("button", { name: "重新读取分类", exact: true }),
  ).toBeEnabled();
  expect(
    fixture.session.apiCalls.filter(
      (call) => call === "GET /api/productcategory/all",
    ),
  ).toHaveLength(fixture.readyCategoryCalls + 1);
  fixture.categoryStatus = 200;
  fixture.categories = [];
  await pane.getByRole("button", { name: "重新读取分类", exact: true }).click();
  await expect(
    pane.getByRole("button", { name: "重新读取分类", exact: true }),
  ).toHaveCount(0);
  await pane.getByRole("combobox", { name: "明细1分类", exact: true }).click();
  await expect(page.locator(".ant-select-dropdown:visible")).toContainText(
    "暂无匹配候选",
  );
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  expect(
    fixture.session.apiCalls.filter(
      (call) => call === "GET /api/productcategory/all",
    ),
  ).toHaveLength(fixture.readyCategoryCalls + 2);
});

test("头与两行统一冻结接纳，零金额不转null，成功后全会话clean", async ({
  page,
}) => {
  const pane = documentPane(page);
  await pane.getByRole("textbox", { name: /单据名称$/ }).fill("已更改头");
  await pane.getByLabel("单据金额", { exact: true }).fill("0.00");
  await pane.getByLabel("明细1名称", { exact: true }).fill("明细甲");
  await pane.getByLabel("明细2数量", { exact: true }).fill("5");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("头与所有明细完整模拟回执已核对接纳");
  await expect(pane).toContainText("头版本：2");
  await expect(pane).toContainText("提交尝试：1");
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await expect(pane.getByLabel("单据金额", { exact: true })).toHaveValue(
    "0.00",
  );
  await expect(
    pane.locator('td[data-column-key="version"]').first(),
  ).toHaveText("2");
  await expect(pane.locator('td[data-column-key="version"]').nth(1)).toHaveText(
    "2",
  );
});

test("确认未生效保留头行草稿可修正，未知锁与导出保留同一冻结材料", async ({
  page,
}) => {
  const pane = documentPane(page);
  await pane.getByRole("textbox", { name: /单据名称$/ }).fill("冻结头");
  await pane.getByLabel("明细1数量", { exact: true }).fill("3");
  await scenario(page, "确认未生效");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("确认头行均未生效");
  await expect(pane.getByRole("textbox", { name: /单据名称$/ })).toBeEditable();
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveValue("3");
  await scenario(page, "响应丢失");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("保存状态：unknown");
  await expect(pane.getByRole("textbox", { name: /单据名称$/ })).toBeDisabled();
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveAttribute(
    "readonly",
    "",
  );
  await expect(
    pane.getByRole("button", { name: "放弃头行修改", exact: true }),
  ).toBeDisabled();
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane).toContainText("保存状态：unknown");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const downloadPromise = page.waitForEvent("download");
  await pane.getByRole("button", { name: "导出恢复记录", exact: true }).click();
  const download = await downloadPromise;
  const record = JSON.parse(await readFile((await download.path())!, "utf8"));
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(record.interfaceOperation).toContain("no HTTP request");
  expect(record.frozenSubmission.header).toMatchObject({
    name: "冻结头",
    Version: 1,
    amount: null,
  });
  expect(record.frozenSubmission.lines).toHaveLength(2);
  expect(record.frozenSubmission.lines[0]).toMatchObject({
    ClientKey: "doc-row-1",
    quantity: 3,
    Version: 1,
  });
  expect(JSON.stringify(record)).not.toMatch(
    /Authorization|access_token|password|CommandId|csrf-test/,
  );
  await pane.getByRole("button", { name: "终止本地恢复", exact: true }).click();
  await cancelDialog(page);
  await expect(pane).toContainText("保存状态：unknown");
  await expect(pane).toContainText("提交尝试：2");
});

/** 新头行入口必须走同一人工保全契约，未知状态不能由批量关闭或一次确认绕过。 */
test("头行合法保全后，两次明确确认才能人工终止原页签", async ({ page }) => {
  const pane = documentPane(page);
  await pane.getByRole("textbox", { name: /单据名称$/ }).fill("待核实头行");
  await pane.getByLabel("明细2数量", { exact: true }).fill("6");
  await scenario(page, "业务失败无法确认");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("保存状态：unknown");
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);

  const downloading = page.waitForEvent("download");
  await pane.getByRole("button", { name: "导出恢复记录", exact: true }).click();
  const download = await downloading;
  const record = JSON.parse(await readFile((await download.path())!, "utf8"));
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(record.frozenSubmission.header.name).toBe("待核实头行");
  expect(record.frozenSubmission.lines[1]).toMatchObject({
    ClientKey: "doc-row-2",
    quantity: 6,
  });

  await pane.getByRole("button", { name: "终止本地恢复", exact: true }).click();
  const retained = page.getByRole("dialog");
  await expect(retained).toContainText("确认记录已妥善保留");
  await expect(retained).toContainText(record.recordId);
  await expect(pane).toContainText("保存状态：unknown");
  await retained
    .getByRole("button", { name: "已妥善保留", exact: true })
    .click();
  const terminated = page.getByRole("dialog", {
    name: "确认终止本地恢复",
    exact: true,
  });
  await expect(terminated).toContainText("确认终止本地恢复");
  await expect(terminated).toContainText(
    "服务器结果仍未确认，不能据此认定失败、回滚或安全重发",
  );
  await expect(pane).toBeVisible();
  await expect(pane).toContainText("提交尝试：1");
  await terminated
    .getByRole("button", { name: "终止并关闭", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(
    page.locator('[data-testid^="document-verification-"]'),
  ).toHaveCount(0);
  await expect(
    page.getByText("工作区为空，请从导航打开页面。", { exact: true }),
  ).toBeVisible();
});

test("保存锁只限原头行页，切换其他 Form 编辑不会被回执接纳", async ({
  page,
}) => {
  const tab = await tabForDocument(page);
  const pane = documentPane(page);
  await pane.getByRole("textbox", { name: /单据名称$/ }).fill("原页提交");
  await pane.getByLabel("明细1数量", { exact: true }).fill("4");
  await scenario(page, "慢成功");
  await pane.getByRole("button", { name: "保存头行", exact: true }).click();
  await expect(pane).toContainText("保存状态：saving");
  await expect(
    pane.getByRole("combobox", { name: "明细1类型", exact: true }),
  ).toBeDisabled();
  await page.getByRole("menuitem", { name: /表单验收/ }).click();
  await basicPane(page).getByLabel("名称", { exact: true }).fill("另一页草稿");
  await expect(basicPane(page).getByLabel("名称", { exact: true })).toHaveValue(
    "另一页草稿",
  );
  await expect(
    page.locator('[data-testid^="document-verification-"]'),
  ).toContainText("头版本：2");
  await expect(basicPane(page)).toContainText("业务修改：是");
  await tab.getByRole("tab").click();
  await expect(documentPane(page)).toContainText(
    "业务修改：否；未提交输入：否",
  );
});

test("字段错误映射用Form路径和ClientKey，未知字段保留页级且不产生dirty", async ({
  page,
}) => {
  const pane = documentPane(page);
  await pane.getByRole("button", { name: "模拟字段错误", exact: true }).click();
  await expect(pane).toContainText("模拟头名称错误");
  await expect(pane).toContainText("模拟明细数量错误");
  await expect(pane).toContainText("未识别字段保留页级错误");
  await expect(pane.getByLabel("明细1数量", { exact: true })).toHaveAttribute(
    "aria-invalid",
    "true",
  );
  await expect(pane.getByLabel("明细2数量", { exact: true })).toHaveAttribute(
    "aria-invalid",
    "false",
  );
  await expect(pane).toContainText("业务修改：否；未提交输入：否");
  await expect(pane).toContainText("提交尝试：0");
});
