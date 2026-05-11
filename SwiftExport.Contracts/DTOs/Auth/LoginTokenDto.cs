namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>JWT 登录成功后的 Token 响应（后端 /api/auth/login 实际返回结构）</summary>
    public class LoginTokenDto
    {
        public string access_token { get; set; }
    }
}
