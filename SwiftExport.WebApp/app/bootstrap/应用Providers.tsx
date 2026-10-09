import { App, ConfigProvider } from "antd";
import zhCN from "antd/locale/zh_CN";
import { QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { createQueryClient } from "../shared/query/查询客户端";
import { 会话Provider } from "../shared/session/会话上下文";
import { useSession } from "../shared/session/会话上下文";
import {
  UserPreferencesProvider,
  useUserPreferences,
} from "../shared/preferences/用户偏好上下文";

/** 偏好只能在 me 核实身份后加载，同一应用复用一个用户上下文。 */
function IdentityPreferences({ children }: { children: ReactNode }) {
  const { user } = useSession();
  return (
    <UserPreferencesProvider userId={user?.userId ?? null}>
      <PreferenceNotice />
      {children}
    </UserPreferencesProvider>
  );
}

/** 存储失败只提示一次并降级内存，不阻断页面编辑或退出。 */
function PreferenceNotice() {
  const { user } = useSession();
  const { storageStatus } = useUserPreferences();
  const { message } = App.useApp();
  const notifiedUser = useRef<string | null>(null);
  useEffect(() => {
    if (
      user?.userId &&
      storageStatus === "memory-only" &&
      notifiedUser.current !== user.userId
    ) {
      notifiedUser.current = user.userId;
      void message.warning("界面偏好暂不能保存，当前页面仍可正常使用。");
    }
  }, [user, storageStatus, message]);
  return null;
}

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
          <会话Provider>
            <IdentityPreferences>{children}</IdentityPreferences>
          </会话Provider>
        </App>
      </ConfigProvider>
    </QueryClientProvider>
  );
}
