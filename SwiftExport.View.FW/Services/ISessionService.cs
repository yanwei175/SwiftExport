using System;

namespace SwiftExport.View.FW.Services
{
    /// <summary>
    /// 会话服务接口，供 ViewModel 依赖注入使用。
    /// </summary>
    public interface ISessionService
    {
        /// <summary>退出登录时触发</summary>
        event Action LoggedOut;

        /// <summary>退出登录</summary>
        void Logout();

        /// <summary>切换用户（退出当前，重新登录）</summary>
        void SwitchUser();
    }
}
