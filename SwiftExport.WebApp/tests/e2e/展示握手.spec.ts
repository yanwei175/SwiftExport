import {
  installBrowserSessionProtocol,
  mockJwtToken,
  authenticationCalls,
} from "./会话模拟";
import { expect, test } from "@playwright/test";
const authToken = mockJwtToken("tester");
import { readFile } from "node:fs/promises";

test.describe("受控浏览器组件的真实展示等待", () => {
  for (const interruption of [
    "401",
    "撤读权",
    "展示隐藏",
    "展示失败",
  ] as const) {
    test(`${interruption}明确拒绝等待，未派发解锁且保留输入`, async ({
      page,
    }) => {
      let meStatus = 200;
      let permissions = ["Products.Product.View", "Products.Product.Edit"];
      const errors: string[] = [];
      const apiPaths: string[] = [];
      page.on("pageerror", (error) => errors.push(error.message));
      page.on("console", (entry) => {
        if (
          entry.type() === "error" &&
          /hydration|hydrated|react error|maximum update depth|uncaught/i.test(
            entry.text(),
          )
        )
          errors.push(entry.text());
      });
      page.on("request", (request) => {
        const path = new URL(request.url()).pathname;
        if (path.startsWith("/api/")) apiPaths.push(path);
      });
      await page.route("**/api/auth/login", (route) =>
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ access_token: authToken }),
        }),
      );
      await page.route("**/api/auth/me", (route) =>
        route.fulfill({
          status: meStatus,
          contentType: "application/json",
          body: JSON.stringify(
            meStatus === 200
              ? { userId: "handshake-user", userName: "tester", permissions }
              : { code: "UNAUTHORIZED" },
          ),
        }),
      );
      // Framework 对 HTML 地址走应用路由；此受控入口由测试精确返回，其模块仍经真实 Vite 转换。
      await page.route(
        (url) =>
          decodeURIComponent(url.pathname) ===
          "/tests/e2e/fixtures/展示握手.html",
        async (route) =>
          route.fulfill({
            contentType: "text/html",
            body: await readFile(
              new URL("./fixtures/展示握手.html", import.meta.url),
              "utf8",
            ),
          }),
      );
      await installBrowserSessionProtocol(page, authToken);
      await page.goto("/tests/e2e/fixtures/展示握手.html");
      await page.getByLabel("用户名", { exact: true }).fill("tester");
      await page
        .getByLabel("密码", { exact: true })
        .fill("handshake-test-password");
      await page.getByRole("button", { name: "登录", exact: true }).click();
      await expect(page.getByTestId("session-status")).toHaveText(
        "authenticated",
      );
      await page
        .getByRole("button", { name: "打开握手验收页", exact: true })
        .click();
      await page
        .getByRole("textbox", { name: "行名称", exact: true })
        .fill("等待中未派发的原始输入");
      await page.getByRole("button", { name: "保存", exact: true }).click();
      // 明确观察 preparing；夹具尚未发出展示就绪，不依靠计时猜测等待阶段。
      await expect(page.getByTestId("host-summary")).toContainText(
        '"preparing":true',
      );
      await expect(
        page.getByText(/保存状态：准备中；提交次数：0/),
      ).toBeVisible();
      if (interruption === "401" || interruption === "撤读权") {
        if (interruption === "401") meStatus = 401;
        else permissions = [];
        await page
          .getByRole("button", { name: "读取身份变化", exact: true })
          .click();
      } else {
        await page
          .getByRole("button", {
            name: interruption === "展示隐藏" ? "中断展示" : "报告展示失败",
            exact: true,
          })
          .click();
      }
      await expect(page.getByTestId("host-summary")).toContainText(
        '"preparing":false',
      );
      await expect(page.getByTestId("host-summary")).toContainText(
        '"saving":false',
      );
      await expect(page.getByTestId("host-summary")).toContainText(
        '"unknown":false',
      );
      await expect(page.getByTestId("host-summary")).toContainText(
        '"hasUncommittedInput":true',
      );
      await expect(page.getByText(/保存状态：就绪；提交次数：0/)).toBeVisible();
      await expect(
        page.getByRole("textbox", { name: "行名称", exact: true }),
      ).toHaveValue("等待中未派发的原始输入");
      expect(
        apiPaths.filter(
          (path) =>
            !authenticationCalls.some((call) => call.endsWith(" " + path)),
        ),
      ).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
});
