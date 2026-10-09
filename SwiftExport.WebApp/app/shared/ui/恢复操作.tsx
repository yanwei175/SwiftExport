import { Button, Card, Space } from "antd";
import type { ReactNode } from "react";

/** 恢复区域只呈现 feature 提供的材料和动作，不判断提交结果、权限或终止资格。 */
export interface RecoveryActionsProps {
  title: string;
  description: ReactNode;
  onExport(): void;
  onTerminate(): void;
}

/** 放在保存状态旁的可见恢复出口；合法保全和两步确认仍由原 feature 会话执行。 */
export function RecoveryActions({
  title,
  description,
  onExport,
  onTerminate,
}: RecoveryActionsProps) {
  return (
    <Card size="small" title={title}>
      <Space orientation="vertical">
        {description}
        <Space wrap>
          <Button onClick={onExport}>导出恢复记录</Button>
          <Button danger onClick={onTerminate}>
            终止本地恢复
          </Button>
        </Space>
      </Space>
    </Card>
  );
}
