import { Alert, Button, Card, Typography } from "antd";
import { LoginForm } from "./登录表单";
import { useSession } from "../../shared/session/会话上下文";

/** 初次登录入口；401 后的重新认证由工作区覆盖层使用同一 LoginForm。 */
export default function LoginPage() {
  const session = useSession();
  return (
    <main className="login-page">
      <Card style={{ width: 380, maxWidth: "calc(100vw - 32px)" }}>
        <Typography.Title level={2}>SwiftExport</Typography.Title>
        <Typography.Paragraph type="secondary">登录工作区</Typography.Paragraph>
        {session.failure && (
          <Alert
            type="warning"
            showIcon
            title={session.failure}
            action={
              <Button
                onClick={() => void session.restore().catch(() => undefined)}
              >
                重试恢复
              </Button>
            }
            style={{ marginBottom: 16 }}
          />
        )}
        <LoginForm />
      </Card>
    </main>
  );
}
