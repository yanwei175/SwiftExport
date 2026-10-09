import { Card, Typography } from "antd";
import type { WorkspaceTabProps } from "../../shared/workspace/页签契约";

/** 首页展示已接入工作区入口，不伪造未来模块或业务统计。 */
export default function HomeTab({ renderDisplay }: WorkspaceTabProps) {
  return renderDisplay(
    <Card>
      <Typography.Title level={3}>欢迎使用 SwiftExport</Typography.Title>
      <Typography.Paragraph>
        从左侧导航打开页面，多个页面可在工作区中切换。
      </Typography.Paragraph>
    </Card>,
  );
}
