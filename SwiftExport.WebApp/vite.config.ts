import { reactRouter } from "@react-router/dev/vite";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
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
      host: "127.0.0.1",
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
