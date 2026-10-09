import { Card, Typography } from "antd";
import { LoginForm } from "./登录表单";

/** 初次登录入口；401 后的重新认证由工作区覆盖层使用同一 LoginForm。 */
export default function LoginPage() {
  return (
    <main className="login-page">
      <Card style={{ width: 380, maxWidth: "calc(100vw - 32px)" }}>
        <Typography.Title level={2}>SwiftExport</Typography.Title>
        <Typography.Paragraph type="secondary">登录工作区</Typography.Paragraph>
        <LoginForm />
      </Card>
    </main>
  );
}
