import { defineConfig } from "@playwright/test";

/** 浏览器验收开启独立验证页；默认发布构建不包含可访问验收入口。 */
export default defineConfig({
  testDir: "tests/e2e",
  fullyParallel: false,
  timeout: 30000,
  expect: { timeout: 10000 },
  use: {
    baseURL: "http://127.0.0.1:4173",
    channel: process.env.PLAYWRIGHT_CHANNEL || "chrome",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: {
    command: "node scripts/启动验收站.mjs",
    url: "http://127.0.0.1:4173",
    reuseExistingServer: false,
    timeout: 120000,
  },
  reporter: [["list"], ["html", { open: "never" }]],
});
