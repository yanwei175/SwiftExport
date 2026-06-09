using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 当前登录用户信息（/api/auth/me 返回结构）。
    /// 包含用户基本信息和角色列表，前端权限判断基于 Roles。
    /// </summary>
    public class MeDto
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string NickName { get; set; }

        /// <summary>用户拥有的所有角色名称列表（如 ["Admin", "Sales"]）</summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>是否为管理员（RoleName 包含 "Admin"，忽略大小写）</summary>
        public bool IsAdmin => Roles != null && Roles.Any(r =>
            string.Equals(r, "Admin", System.StringComparison.OrdinalIgnoreCase));
    }
}
