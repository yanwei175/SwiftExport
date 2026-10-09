import { useState } from "react";
import { Alert, Button, Form, Input } from "antd";
import type { SwiftExportContractsDTOsAuthLoginRequestDto as LoginRequestDto } from "../../shared/api/generated/client";
import { describeApiError } from "../../shared/api/响应错误";
import { useSession } from "../../shared/session/会话上下文";

/** 登录及工作区重认证共用 Form；字段值只属于 Ant Form，密码不持久化。 */
export function LoginForm() {
  const [form] = Form.useForm<LoginRequestDto>();
  const { login } = useSession();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /** 登录成功须完成 me 核实；失败保留用户名但清理敏感密码字段。 */
  async function submit(values: LoginRequestDto): Promise<void> {
    setBusy(true);
    setError(null);
    try {
      await login(values);
    } catch (cause) {
      setError(describeApiError(cause));
    } finally {
      form.resetFields(["password"]);
      setBusy(false);
    }
  }

  return (
    <Form
      form={form}
      layout="vertical"
      disabled={busy}
      onFinish={submit}
      requiredMark={false}
    >
      {error && (
        <Alert
          title={error}
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
        />
      )}
      <Form.Item
        label="用户名"
        name="userName"
        rules={[
          { required: true, whitespace: true, message: "请输入用户名。" },
        ]}
      >
        <Input autoComplete="username" autoFocus />
      </Form.Item>
      <Form.Item
        label="密码"
        name="password"
        rules={[{ required: true, message: "请输入密码。" }]}
      >
        <Input.Password autoComplete="current-password" />
      </Form.Item>
      <Button type="primary" htmlType="submit" block loading={busy}>
        登录
      </Button>
    </Form>
  );
}
