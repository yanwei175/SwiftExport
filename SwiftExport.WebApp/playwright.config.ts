import { defineConfig } from "@playwright/test";

/** 浏览器验收开启独立验证页；默认发布构建不包含可访问验收入口。 */
export default defineConfig({
  testDir: "tests/e2e",
  fullyParallel: false,
  timeout: 30000,
  expect: { timeout: 10000 },
  use: {
    baseURL: "https://localhost:4173",
    channel: process.env.PLAYWRIGHT_CHANNEL || "chrome",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    // 真实Chrome必须接受本机已受信任的开发证书，不绕过浏览器TLS验证。
    ignoreHTTPSErrors: false,
  },
  webServer: {
    command: "node scripts/启动验收站.mjs",
    url: "https://localhost:4173",
    // 仅就绪探测的Node进程未必读取系统证书库；实际浏览器仍严格验证上面的TLS配置。
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 120000,
  },
  reporter: [["list"], ["html", { open: "never" }]],
});
