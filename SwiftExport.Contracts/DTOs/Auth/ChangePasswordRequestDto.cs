namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>当前用户验证旧密码并修改本人密码的请求。</summary>
    public class ChangePasswordRequestDto
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
