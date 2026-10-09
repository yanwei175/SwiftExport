import { spawn } from "node:child_process";
import { chromium } from "playwright";
import assert from "node:assert/strict";
import fs from "node:fs/promises";

// 启动本项目静态产物预览，核对真实SPA文件及页面/资源/API回退边界。
const child = spawn(process.execPath, ["scripts/静态预览.mjs"], {
  stdio: ["ignore", "pipe", "pipe"],
  env: { ...process.env, PORT: "4174" },
});
let browser;
try {
  await new Promise((resolve, reject) => {
    child.stdout.once("data", resolve);
    child.once("error", reject);
    child.once("exit", (code) => reject(new Error(`预览站提前退出：${code}`)));
  });
  const origin = "http://127.0.0.1:4174";
  for (const route of ["/", "/workspace", "/login", "/unknown-page"]) {
    const response = await fetch(`${origin}${route}`);
    assert.equal(response.status, 200);
    assert.match(response.headers.get("cache-control"), /no-cache/);
    assert.match(await response.text(), /正在加载 SwiftExport/);
  }
  for (const route of [
    "/api",
    "/api/auth/me",
    "/files",
    "/files/missing",
    "/assets/missing.js",
    "/missing.css",
  ])
    assert.equal((await fetch(`${origin}${route}`)).status, 404);
  const assets = await fs.readdir("build/client/assets");
  assert.equal(
    assets.some((file) => file.includes("验证页签入口")),
    false,
    "默认发布不能输出验收模块",
  );
  const assetResponse = await fetch(
    `${origin}/assets/${assets.find((file) => file.endsWith(".js"))}`,
  );
  assert.match(assetResponse.headers.get("cache-control"), /immutable/);
  browser = await chromium.launch({
    channel: process.env.PLAYWRIGHT_CHANNEL || "chrome",
    headless: true,
  });
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (entry) => {
    if (
      entry.type() === "error" &&
      /hydration|uncaught|maximum update depth/i.test(entry.text())
    )
      errors.push(entry.text());
  });
  // 此 HTTP 静态站只核对生产页面组合；Cookie/HTTPS 恢复由浏览器专项验收覆盖。
  const token = [
    Buffer.from('{"alg":"HS256"}').toString("base64url"),
    Buffer.from(
      JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 43_200 }),
    ).toString("base64url"),
    "static-test-only",
  ].join(".");
  let restorable = false;
  await page.route("**/api/auth/login", (route) => {
    restorable = true;
    return route.fulfill({ json: { access_token: token } });
  });
  await page.route("**/api/auth/browser-session/*", (route) => {
    const path = new URL(route.request().url()).pathname;
    assert.equal(route.request().headers().authorization, undefined);
    if (path.endsWith("/csrf"))
      return route.fulfill({ json: { requestToken: "static-csrf-only" } });
    if (path.endsWith("/restore"))
      return route.fulfill({
        status: restorable ? 200 : 401,
        json: restorable
          ? { access_token: token }
          : { code: "UNAUTHENTICATED" },
      });
    return route.abort("blockedbyclient");
  });
  await page.route("**/api/auth/me", (route) =>
    route.fulfill({
      json: {
        userId: "static-verification-user",
        userName: "静态验收",
        permissions: [],
      },
    }),
  );
  await page.goto(`${origin}/workspace`);
  await page.getByLabel("用户名", { exact: true }).fill("静态验收");
  await page.getByLabel("密码", { exact: true }).fill("local-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  await page.getByText("欢迎使用 SwiftExport", { exact: true }).waitFor();
  assert.equal(
    await page.getByRole("menuitem", { name: /骨架验收/ }).count(),
    0,
  );
  assert.deepEqual(errors, []);
  process.stdout.write(
    "静态发布检查通过：深链接、资源/API排除、缓存、生产登录/hydration与验收模块隔离。\n",
  );
} finally {
  await browser?.close();
  child.kill();
}
