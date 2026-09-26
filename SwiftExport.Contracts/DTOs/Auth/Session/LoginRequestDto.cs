namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>登录请求 DTO</summary>
    public class LoginRequestDto
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }
}
