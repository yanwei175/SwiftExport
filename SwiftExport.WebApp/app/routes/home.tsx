import { Navigate } from "react-router";
/** 应用首页统一定位工作区，会话访问检查由目标入口执行。 */
export default function InitialRoute() {
  return <Navigate to="/workspace" replace />;
}
