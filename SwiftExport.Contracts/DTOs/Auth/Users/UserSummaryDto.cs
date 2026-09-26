using System;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>供管理员选择权限目标的最小用户摘要，不包含凭据和联系方式。</summary>
    public class UserSummaryDto
    {
        public Guid Uid { get; set; }
        public string UserName { get; set; }
        public string NickName { get; set; }
        public bool IsAdmin { get; set; }
    }
}
