using System;
using System.Linq;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>从已验证会话读取显示名称和权限，仅用于客户端体验。</summary>
    public sealed class CurrentUserService
    {
        private readonly SessionService session;
        /// <summary>绑定当前会话。</summary>
        public CurrentUserService(SessionService session) { this.session = session; }
        public bool IsAuthenticated { get { return session.Capture().User != null; } }
        public string DisplayName
        {
            get { var user = session.Capture().User; return user == null ? "未登录" : string.IsNullOrWhiteSpace(user.NickName) ? user.UserName : user.NickName; }
        }
        /// <summary>按服务端稳定权限键判断，不通过角色名推导授权。</summary>
        public bool HasPermission(string key) { return session.Capture().User?.Permissions?.Contains(key, StringComparer.Ordinal) == true; }
    }
}
