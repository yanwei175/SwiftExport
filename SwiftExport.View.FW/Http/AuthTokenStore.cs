using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.View.FW.Http
{
    /// <summary>
    /// 全局 JWT Token 存储。
    /// 登录成功后调用 <see cref="Set"/>，/me 拉取角色后调用 <see cref="SetRoles"/>，登出时调用 <see cref="Clear"/>。
    /// WPF 单进程生命周期内使用静态存储，无需 DI。
    /// </summary>
    public static class AuthTokenStore
    {
        private static string _token = string.Empty;

        /// <summary>当前用户名（登录名）</summary>
        public static string CurrentUserName { get; private set; } = string.Empty;

        /// <summary>当前用户昵称（显示用）</summary>
        public static string CurrentNickName { get; private set; } = string.Empty;

        /// <summary>当前用户 ID</summary>
        public static string CurrentUserId { get; private set; } = string.Empty;

        /// <summary>当前用户拥有的角色名列表（来自 /api/auth/me）</summary>
        public static IReadOnlyList<string> Roles { get; private set; } = new List<string>();

        /// <summary>是否为管理员（RoleName 包含 "Admin"，忽略大小写）</summary>
        public static bool IsAdmin => Roles.Any(r =>
            string.Equals(r, "Admin", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>是否已登录（有 Token）</summary>
        public static bool IsLoggedIn => !string.IsNullOrEmpty(_token);

        /// <summary>登录成功后保存 Token 和基本用户信息</summary>
        public static void Set(string token, string userName = "", string userId = "", string nickName = "")
        {
            _token = token ?? string.Empty;
            CurrentUserName = userName;
            CurrentUserId = userId;
            CurrentNickName = string.IsNullOrEmpty(nickName) ? userName : nickName;
        }

        /// <summary>从 /api/auth/me 获取角色后更新（登录后异步调用）</summary>
        public static void SetRoles(IEnumerable<string> roles)
        {
            Roles = new List<string>(roles ?? Enumerable.Empty<string>());
        }

        /// <summary>获取当前 Bearer Token</summary>
        public static string Get() => _token;

        /// <summary>判断是否拥有指定角色（忽略大小写）</summary>
        public static bool HasRole(string roleName) =>
            Roles.Any(r => string.Equals(r, roleName, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>登出时清除所有状态</summary>
        public static void Clear()
        {
            _token = string.Empty;
            CurrentUserName = string.Empty;
            CurrentNickName = string.Empty;
            CurrentUserId = string.Empty;
            Roles = new List<string>();
        }
    }
}
