import { Alert, Empty, Space, Spin } from "antd";
import type { ReactNode } from "react";

/** 公共页面反馈只负责呈现，重读、授权判断和草稿保护由调用方负责。 */
export interface PageStateProps {
  kind: "loading" | "empty" | "error" | "forbidden";
  title?: string;
  description?: ReactNode;
  action?: ReactNode;
}

/** 统一加载、空数据、错误和无权限界面；不自行请求、重试或重置编辑数据。 */
export function PageState({
  kind,
  title,
  description,
  action,
}: PageStateProps) {
  if (kind === "loading")
    return (
      <div
        className="page-state"
        role="status"
        aria-label={title ?? "正在加载页面"}
      >
        <Space orientation="vertical" align="center">
          <Spin />
          <span>{title ?? "正在加载页面…"}</span>
          {description}
        </Space>
      </div>
    );
  if (kind === "empty")
    return (
      <div className="page-state">
        <Empty description={description ?? title ?? "暂无数据"}>{action}</Empty>
      </div>
    );
  return (
    <Alert
      showIcon
      type={kind === "error" ? "error" : "warning"}
      role="alert"
      title={
        title ??
        (kind === "error" ? "页面暂时无法显示" : "当前没有页面读取权限")
      }
      description={description}
      action={action}
    />
  );
}
