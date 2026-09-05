using System;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>管理员重置指定普通用户密码的请求。</summary>
    public class AdminResetPasswordRequestDto
    {
        public Guid UserId { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
