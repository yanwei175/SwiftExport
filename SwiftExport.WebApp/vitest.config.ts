import { defineConfig } from "vitest/config";

/** 纯状态和传输验证不连接业务服务器，也不替代真实浏览器生命周期证据。 */
export default defineConfig({
  test: {
    include: ["app/**/*.test.{ts,tsx}", "tests/unit/**/*.test.ts"],
    environment: "node",
  },
});
