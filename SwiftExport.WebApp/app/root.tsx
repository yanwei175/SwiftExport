import { Links, Meta, Outlet, Scripts, ScrollRestoration } from "react-router";
import type { ReactNode } from "react";
import { ApplicationProviders } from "./bootstrap/应用Providers";
import "antd/dist/reset.css";
import "./shared/styles/基础样式.css";

/** 静态根渲染只建立文档结构，不访问浏览器身份或业务接口。 */
export function Layout({ children }: { children: ReactNode }) {
  return (
    <html lang="zh-CN">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>SwiftExport</title>
        <Meta />
        <Links />
      </head>
      <body>
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}
/** 框架根入口保留唯一 Router。 */
export default function Root() {
  return (
    <ApplicationProviders>
      <Outlet />
    </ApplicationProviders>
  );
}
/** SPA 的首次构建与加载不读取身份。 */
export function HydrateFallback() {
  return <main>正在加载 SwiftExport…</main>;
}
/** 应用级异常界面不回显凭据或服务端异常详情。 */
export function ErrorBoundary() {
  return <main role="alert">应用暂时无法显示，请刷新后重试。</main>;
}
