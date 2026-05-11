using SwiftExport.View.FW.Http;
using System;

namespace SwiftExport.View.FW.Services
{
    /// <summary>
    /// 会话服务：管理登录、退出、切换用户的全局逻辑。
    /// 退出时清除 AuthTokenStore，并触发事件通知 Shell 重新显示登录窗口。
    /// </summary>
    public class SessionService : ISessionService
    {
        /// <summary>
        /// 退出登录时触发（Shell 监听此事件，响应显示登录窗口）。
        /// </summary>
        public event Action LoggedOut;

        /// <summary>
        /// 执行退出登录：清空 Token 和用户信息，通知 Shell。
        /// </summary>
        public void Logout()
        {
            AuthTokenStore.Clear();
            LoggedOut?.Invoke();
        }

        /// <summary>
        /// 切换用户：等同于退出后重新登录，复用 Logout 即可。
        /// </summary>
        public void SwitchUser() => Logout();
    }
}
