import { reactRouter } from "@react-router/dev/vite";
import { defineConfig, loadEnv } from "vite";

/** 开发/验收以受信任localhost证书提供HTTPS；build只读取构建配置，不准备证书或访问浏览器。 */
export default defineConfig(async ({ mode, command, isPreview }) => {
  let https: { cert: Buffer; key: Buffer } | undefined;
  // Framework 的构建期根渲染会再调用 Vite preview（command 也为 serve）；仅开发站准备证书。
  if (command === "serve" && !isPreview) {
    // 工具脚本由Node执行，窄类型声明限定其实际输出，不将证书/私钥带入客户端模块。
    const { prepareDevelopmentCertificate } = (await import(
      new URL("./scripts/准备开发证书.mjs", import.meta.url).href
    )) as {
      prepareDevelopmentCertificate: () => { cert: Buffer; key: Buffer };
    };
    https = prepareDevelopmentCertificate();
  }
  // 与后端 https 启动配置对齐，避免307把浏览器登录请求带出同源代理。
  const apiTarget =
    loadEnv(mode, process.cwd(), "").API_PROXY_TARGET ||
    "https://localhost:7286";
  const apiUrl = new URL(apiTarget);
  const localDevelopmentCertificate =
    apiUrl.protocol === "https:" &&
    ["localhost", "127.0.0.1", "[::1]"].includes(apiUrl.hostname);
  return {
    plugins: [reactRouter()],
    resolve: {
      tsconfigPaths: true,
    },
    optimizeDeps: {
      include: [
        "react",
        "react-dom",
        "react/jsx-runtime",
        "react-router",
        "antd",
        "antd/locale/zh_CN",
        "@ant-design/icons",
        "@tanstack/react-query",
      ],
    },
    server: {
      host: "localhost",
      port: 5173,
      strictPort: true,
      https,
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
          // 仅本机HTTPS开发证书交由代理接纳；其他目标仍验证证书。
          secure: !localDevelopmentCertificate,
        },
      },
    },
  };
});
