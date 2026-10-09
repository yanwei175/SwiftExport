import { Modal } from "antd";
import { Navigate, useLocation } from "react-router";
import { LoginForm } from "../features/auth/登录表单";
import { useSession } from "../shared/session/会话上下文";
import { WorkspaceProvider } from "../workspace/工作区Provider";
import { WorkspaceLayout } from "../layouts/工作区布局";

/** 稳定工作区入口：401 不跳登录销毁草稿，换身份才重建整个工作区。 */
export default function WorkspaceRoute() {
  const session = useSession();
  const location = useLocation();
  if (session.status === "anonymous")
    return (
      <Navigate
        to="/login"
        replace
        state={{ returnTo: `${location.pathname}${location.search}` }}
      />
    );
  return (
    <WorkspaceProvider key={session.identityEpoch}>
      <WorkspaceLayout />
      <Modal
        title="重新认证"
        open={session.status === "reauthenticating"}
        footer={null}
        closable={false}
        mask={{ closable: false }}
      >
        <p>会话已失效，请重新登录。原页签输入仍保留。</p>
        <LoginForm />
      </Modal>
    </WorkspaceProvider>
  );
}
