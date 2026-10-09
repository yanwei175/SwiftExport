import { expect, test } from "@playwright/test";
import type { Locator, Page } from "@playwright/test";
import { readFile } from "node:fs/promises";

type ProbeKind = "form" | "grid";

/** 本场景只模拟认证读写，任何其他 API 都被拒绝，避免恢复验收访问业务系统。 */
async function prepareRecoveryProbe(page: Page, kind: ProbeKind) {
  const apiCalls: string[] = [];
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
      const pathname = new URL(route.request().url()).pathname;
      apiCalls.push(`${route.request().method()} ${pathname}`);
      if (pathname === "/api/auth/login") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ access_token: "recovery-test-token-only" }),
        });
        return;
      }
      if (pathname === "/api/auth/me") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            userId: "recovery-user",
            userName: "tester",
            nickName: "恢复验收用户",
            permissions: ["Products.Product.View", "Products.Product.Edit"],
          }),
        });
        return;
      }
      await route.abort("blockedbyclient");
    },
  );
  await page.goto("/login");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page
    .getByLabel("密码", { exact: true })
    .fill("recovery-test-password-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  const menuItem = page.getByRole("menuitem", {
    name: kind === "form" ? "表单验收" : "列表验收",
    exact: true,
  });
  if (!(await menuItem.isVisible()))
    await page.getByRole("menuitem", { name: /骨架验收/ }).click();
  await menuItem.click();
  const pane = activePane(page);
  await (
    kind === "form"
      ? pane.getByLabel("名称", { exact: true })
      : pane.getByRole("textbox", { name: "行名称", exact: true })
  ).fill("需要保全的冻结内容");
  await pane.getByRole("combobox", { name: "保存场景" }).click();
  await page
    .locator(".ant-select-dropdown:visible")
    .getByText("响应丢失", { exact: true })
    .click();
  await pane.getByRole("button", { name: "保存", exact: true }).click();
  await expect(pane.getByText(/保存状态：结果未知；提交次数：1/)).toBeVisible();
  return { apiCalls, errors };
}

/** 定位真实可见 Activity 子树，隐藏页 DOM 不作为恢复入口可见证据。 */
function activePane(page: Page): Locator {
  return page.locator('[data-testid^="verification-"]:visible');
}

/** 取消任一步确认后等待弹窗真正退出，再复核页签未知状态。 */
async function cancelConfirmation(page: Page, title: string) {
  await page
    .getByRole("dialog", { name: title, exact: true })
    .getByRole("button", { name: "取消", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(
    activePane(page).getByText(/保存状态：结果未知；提交次数：1/),
  ).toBeVisible();
}

test.describe("恢复出口在常见窗口内可见且仍受保全约束", () => {
  test.use({ viewport: { width: 1280, height: 720 } });

  for (const kind of ["form", "grid"] as const) {
    test(`${kind} 结果未知后无需滚动即可找到恢复入口`, async ({ page }) => {
      const fixture = await prepareRecoveryProbe(page, kind);
      const pane = activePane(page);
      // 断言不会执行 scrollIntoView，必须观察保存返回后实际所在的视口。
      await expect(
        pane.getByRole("button", { name: "导出恢复记录", exact: true }),
      ).toBeInViewport({ ratio: 1 });
      await expect(
        pane.getByRole("button", { name: "终止本地恢复", exact: true }),
      ).toBeInViewport({ ratio: 1 });
      expect(
        fixture.apiCalls.filter(
          (call) =>
            !["POST /api/auth/login", "GET /api/auth/me"].includes(call),
        ),
      ).toEqual([]);
      expect(fixture.errors).toEqual([]);
    });

    test(`${kind} 未导出、未确认及批量关闭不能绕过人工恢复`, async ({
      page,
    }) => {
      const fixture = await prepareRecoveryProbe(page, kind);
      const tab = page.getByRole("tab", {
        name: kind === "form" ? /表单验收/ : /列表验收/,
      });
      await activePane(page)
        .getByRole("button", { name: "终止本地恢复", exact: true })
        .click();
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(
        activePane(page).getByText(/尚未完成合法恢复记录导出/),
      ).toBeVisible();
      await page.getByRole("button", { name: "关闭全部", exact: true }).click();
      await expect(tab).toHaveCount(1);
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(
        activePane(page).getByRole("button", { name: "保存", exact: true }),
      ).toBeDisabled();

      const downloadEvent = page.waitForEvent("download");
      await activePane(page)
        .getByRole("button", { name: "导出恢复记录", exact: true })
        .click();
      const download = await downloadEvent;
      const downloadPath = await download.path();
      expect(downloadPath).not.toBeNull();
      const record = JSON.parse(await readFile(downloadPath!, "utf8")) as {
        recordId: string;
      };
      expect(download.suggestedFilename()).toBe(
        `SwiftExport-恢复记录-${record.recordId}.json`,
      );

      await activePane(page)
        .getByRole("button", { name: "终止本地恢复", exact: true })
        .click();
      await expect(
        page.getByRole("dialog", { name: "确认记录已妥善保留", exact: true }),
      ).toContainText(record.recordId);
      await cancelConfirmation(page, "确认记录已妥善保留");
      await page.getByRole("button", { name: "关闭全部", exact: true }).click();
      await expect(tab).toHaveCount(1);
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
      await cancelConfirmation(page, "确认终止本地恢复");
      await expect(tab).toHaveCount(1);

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
      await expect(tab).toHaveCount(0);
      expect(
        fixture.apiCalls.filter(
          (call) =>
            !["POST /api/auth/login", "GET /api/auth/me"].includes(call),
        ),
      ).toEqual([]);
      expect(fixture.errors).toEqual([]);
    });
  }
});
