import { App, ConfigProvider } from "antd";
import zhCN from "antd/locale/zh_CN";
import { QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import type { ReactNode } from "react";
import { createQueryClient } from "../shared/query/查询客户端";
import { 会话Provider } from "../shared/session/会话上下文";

/** 唯一应用组合入口：Query、主题语言、Ant 消息上下文与内存会话。 */
export function ApplicationProviders({ children }: { children: ReactNode }) {
  const [queryClient] = useState(createQueryClient);
  return (
    <QueryClientProvider client={queryClient}>
      <ConfigProvider
        locale={zhCN}
        button={{ autoInsertSpace: false }}
        theme={{
          token: {
            colorPrimary: "#176d88",
            borderRadius: 6,
            fontFamily: '"Segoe UI", "Microsoft YaHei", sans-serif',
          },
        }}
      >
        <App>
          <会话Provider>{children}</会话Provider>
        </App>
      </ConfigProvider>
    </QueryClientProvider>
  );
}
