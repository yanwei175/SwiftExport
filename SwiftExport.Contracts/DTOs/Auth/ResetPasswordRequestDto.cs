namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 重置密码请求 DTO。
    /// VerificationCode 按业务约定使用“管理员密码”进行校验。
    /// </summary>
    public class ResetPasswordRequestDto
    {
        public string UserName { get; set; }
        public string NewPassword { get; set; }
        public string VerificationCode { get; set; }
    }
}
