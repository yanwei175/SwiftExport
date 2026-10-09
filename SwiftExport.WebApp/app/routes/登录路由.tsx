import { Navigate, useLocation } from "react-router";
import LoginPage from "../features/auth/登录页";
import { useSession } from "../shared/session/会话上下文";

/** 初次登录使用独立路由，已有会话的重认证由工作区覆盖层完成。 */
export default function LoginRoute() {
  const { status } = useSession();
  const location = useLocation();
  const proposed: unknown = location.state?.returnTo;
  const returnTo =
    typeof proposed === "string" && /^\/workspace(?:\?|$)/.test(proposed)
      ? proposed
      : "/workspace";
  return status === "authenticated" ? (
    <Navigate to={returnTo} replace />
  ) : (
    <LoginPage />
  );
}
