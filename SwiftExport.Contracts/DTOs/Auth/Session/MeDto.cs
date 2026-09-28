using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>向客户端提供当前账号、有效角色与权限快照；服务端请求仍实时校验权限。</summary>
    public class MeDto
    {
        /// <summary>当前用户标识。</summary>
        public string UserId { get; set; }

        /// <summary>登录名。</summary>
        public string UserName { get; set; }

        /// <summary>显示名称。</summary>
        public string NickName { get; set; }

        /// <summary>有效角色的当前显示名称，仅供界面展示。</summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>有效角色的稳定编码。</summary>
        public List<string> RoleCodes { get; set; } = new List<string>();

        /// <summary>有效的已知权限键。</summary>
        public List<string> Permissions { get; set; } = new List<string>();

        /// <summary>服务端计算的有效内置管理员身份。</summary>
        public bool IsAdmin { get; set; }
    }
}
