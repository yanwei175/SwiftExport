import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import { readFile } from "node:fs/promises";

/** 只验收真实 Pro/Ant 控件与本地 Form；夹具不允许发送认证或业务 API。 */
interface ProObservations {
  apiCalls: string[];
  errors: string[];
}

const observations = new WeakMap<Page, ProObservations>();

/** 独立 HTML 使用真实 Vite 模块，精准拦截 /api/，不发布测试夹具。 */
async function openProFixture(page: Page): Promise<void> {
  const observed: ProObservations = { apiCalls: [], errors: [] };
  observations.set(page, observed);
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
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    (route) => route.abort("blockedbyclient"),
  );
  await page.route(
    (url) =>
      decodeURIComponent(url.pathname) === "/tests/e2e/fixtures/Pro组件.html",
    async (route) =>
      route.fulfill({
        contentType: "text/html",
        body: await readFile(
          new URL("./fixtures/Pro组件.html", import.meta.url),
          "utf8",
        ),
      }),
  );
  await page.goto("/tests/e2e/fixtures/Pro组件.html");
  await expect(page.getByLabel("Pro单据名称", { exact: true })).toBeVisible();
}

/** 保留 raw 的唯一 Form 值从可观察快照读取，不在测试端建立另一份可写行状态。 */
async function currentValues(page: Page): Promise<Record<string, unknown>> {
  return JSON.parse((await page.getByTestId("pro-current").textContent())!);
}

test.beforeEach(async ({ page }) => {
  await openProFixture(page);
});

test.afterEach(({ page }) => {
  const observed = observations.get(page)!;
  expect(observed.apiCalls, "ProComponents 夹具不得发送任何 API").toEqual([]);
  expect(
    observed.errors,
    "真实 Pro/Ant 组合不得造成页面或 hydration 异常",
  ).toEqual([]);
});

test("EditableProTable 无效文本留在唯一 Form，隐藏保留且校验失败不提交", async ({
  page,
}) => {
  await expect(page.getByTestId("pro-owner")).toContainText(
    "同一外层 Ant Form",
  );
  const quantity = page.getByLabel("Pro行1数量", { exact: true });
  const baseline = await quantity.inputValue();
  await quantity.fill("1e");
  await expect(quantity).toBeFocused();
  await expect(page.getByTestId("pro-summary")).toContainText(
    '"hasUncommittedInput":true',
  );
  expect(JSON.stringify(await currentValues(page))).toContain(
    '"quantity":"1e"',
  );
  await page
    .getByRole("button", { name: "隐藏 Pro 展示", exact: true })
    .click();
  await expect(quantity).toBeHidden();
  await expect(page.getByTestId("pro-summary")).toContainText(
    '"hasUncommittedInput":true',
  );
  await page
    .getByRole("button", { name: "显示 Pro 展示", exact: true })
    .click();
  await expect(quantity).toHaveValue("1e");
  await page.getByRole("button", { name: "统一保存 Pro", exact: true }).click();
  await expect(page.getByTestId("pro-save-count")).toHaveText("0");
  await expect(quantity).toHaveValue("1e");
  await expect(page.getByTestId("pro-notice")).not.toHaveText("");
  await quantity.press("Escape");
  await expect(quantity).toHaveValue(baseline);
  await expect(page.getByTestId("pro-summary")).toContainText('"dirty":false');
  await expect(page.getByTestId("pro-summary")).toContainText(
    '"hasUncommittedInput":false',
  );
});

test("ProForm 头与 EditableProTable 两行统一校验，一次提交后全会话 clean", async ({
  page,
}) => {
  await page.getByLabel("Pro单据名称", { exact: true }).fill("统一 Pro 头");
  await page.getByLabel("Pro行1名称", { exact: true }).fill("Pro 明细甲");
  await page.getByLabel("Pro行2数量", { exact: true }).fill("12");
  await expect(page.getByTestId("pro-summary")).toContainText('"dirty":true');
  await page.getByRole("button", { name: "统一保存 Pro", exact: true }).click();
  await expect(page.getByTestId("pro-save-count")).toHaveText("1");
  await expect(page.getByTestId("pro-summary")).toContainText('"dirty":false');
  await expect(page.getByTestId("pro-summary")).toContainText(
    '"hasUncommittedInput":false',
  );
  await expect(page.getByLabel("Pro行1名称", { exact: true })).toHaveValue(
    "Pro 明细甲",
  );
  await expect(page.getByLabel("Pro行2数量", { exact: true })).toHaveValue(
    "12",
  );
  expect(JSON.stringify(await currentValues(page))).toContain("统一 Pro 头");
  await page.getByLabel("Pro行1数量", { exact: true }).fill("1.");
  await page.getByRole("button", { name: "统一保存 Pro", exact: true }).click();
  await expect(page.getByTestId("pro-save-count")).toHaveText("1");
  await expect(page.getByLabel("Pro行1数量", { exact: true })).toHaveValue(
    "1.",
  );
});

test("Ant 原生多选 Select 选择与搜索分离，隐藏保留且可清空", async ({
  page,
}) => {
  const control = page.getByRole("combobox", {
    name: "Pro多选分类",
    exact: true,
  });
  const selectRoot = page.locator(".ant-select").filter({ has: control });
  await control.click();
  const dropdown = page.locator(".ant-select-dropdown:visible");
  await dropdown.getByText("分类A", { exact: true }).click();
  await dropdown.getByText("分类B", { exact: true }).click();
  await page.keyboard.press("Escape");
  await expect(selectRoot).toContainText("分类A");
  await expect(selectRoot).toContainText("分类B");
  const selected = await currentValues(page);
  await control.fill("不会成为业务选值的搜索词");
  await page.keyboard.press("Escape");
  expect(await currentValues(page)).toEqual(selected);
  await page
    .getByRole("button", { name: "隐藏 Pro 展示", exact: true })
    .click();
  await page
    .getByRole("button", { name: "显示 Pro 展示", exact: true })
    .click();
  expect(await currentValues(page)).toEqual(selected);
  await page
    .getByRole("button", { name: "清空 Pro 多选分类", exact: true })
    .click();
  const cleared = await currentValues(page);
  expect(cleared).not.toEqual(selected);
  await expect(selectRoot).not.toContainText("分类A");
  await expect(selectRoot).not.toContainText("分类B");
});

test("DragSortTable 拖动真实把手更新顺序，稳定 UID 与行内容保留", async ({
  page,
}) => {
  const before = JSON.parse(
    (await page.getByTestId("pro-drag-order").textContent())!,
  ) as string[];
  expect(before).toEqual(["a", "b", "c"]);
  const source = page.getByTestId("pro-drag-uid-a");
  const destination = page.getByTestId("pro-drag-uid-c");
  await source.scrollIntoViewIfNeeded();
  const sourceBox = (await source.boundingBox())!;
  const targetBox = (await destination.boundingBox())!;
  await page.mouse.move(
    sourceBox.x + sourceBox.width / 2,
    sourceBox.y + sourceBox.height / 2,
  );
  await page.mouse.down();
  await page.mouse.move(
    targetBox.x + targetBox.width / 2,
    targetBox.y + targetBox.height / 2,
    { steps: 18 },
  );
  await page.mouse.up();
  await expect(page.getByTestId("pro-drag-order")).not.toHaveText(
    JSON.stringify(before),
  );
  const after = JSON.parse(
    (await page.getByTestId("pro-drag-order").textContent())!,
  ) as string[];
  expect([...after].sort()).toEqual(["a", "b", "c"]);
  for (const key of before)
    await expect(page.getByTestId(`pro-drag-uid-${key}`)).toBeVisible();
  await expect(page.getByTestId("pro-save-count")).toHaveText("0");
});

test("ProDescriptions 与 ProList 复用展示能力，不建立额外写入链", async ({
  page,
}) => {
  await expect(page.getByTestId("pro-descriptions")).toBeVisible();
  await expect(page.getByTestId("pro-list")).toBeVisible();
  await expect(page.getByTestId("pro-descriptions")).toContainText(
    "只读详情示例",
  );
  for (const label of ["排序A", "排序B", "排序C"]) {
    await expect(page.getByTestId("pro-list")).toContainText(label);
  }
  await expect(page.getByTestId("pro-save-count")).toHaveText("0");
});
