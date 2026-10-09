import { Alert, Button, Card, Space, Spin, Typography } from "antd";
import { useSession } from "../../shared/session/会话上下文";

/** 初次恢复和退出确认界面不开放业务内容；网络失败提供明确重试入口。 */
export function SessionStatusPage() {
  const session = useSession();
  const exiting =
    session.status === "signing-out" || session.status === "logout-unconfirmed";
  const pending =
    session.status === "restoring" || session.status === "signing-out";
  return (
    <main className="login-page">
      <Card style={{ width: 420, maxWidth: "calc(100vw - 32px)" }}>
        <Typography.Title level={2}>SwiftExport</Typography.Title>
        {pending ? (
          <Space>
            <Spin />
            <span>{exiting ? "正在确认退出…" : "正在恢复登录…"}</span>
          </Space>
        ) : (
          <>
            <Alert
              type="warning"
              showIcon
              title={session.failure ?? "登录恢复失败，请重试。"}
            />
            <Button
              style={{ marginTop: 16 }}
              onClick={() =>
                void (exiting ? session.logout() : session.restore()).catch(
                  () => undefined,
                )
              }
            >
              {exiting ? "重试退出" : "重试恢复登录"}
            </Button>
          </>
        )}
      </Card>
    </main>
  );
}
