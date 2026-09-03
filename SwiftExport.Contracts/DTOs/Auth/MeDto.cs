using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 当前登录用户信息（/api/auth/me 返回结构）。
    /// 分别包含用户基本信息、角色列表和用于客户端体验提示的稳定权限键。
    /// </summary>
    public class MeDto
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string NickName { get; set; }

        /// <summary>用户拥有的所有角色名称列表（如 ["Admin", "Sales"]）</summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>用户拥有的稳定权限键；服务端仍须在每个命令执行时实时校验。</summary>
        public List<string> Permissions { get; set; } = new List<string>();

        /// <summary>是否为管理员（RoleName 包含 "Admin"，忽略大小写）</summary>
        public bool IsAdmin => Roles != null && Roles.Any(r =>
            string.Equals(r, "Admin", System.StringComparison.OrdinalIgnoreCase));
    }
}
