import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/home.tsx"),
  route("login", "routes/登录路由.tsx"),
  route("workspace", "routes/工作区路由.tsx"),
  route("*", "routes/未找到路由.tsx"),
] satisfies RouteConfig;
