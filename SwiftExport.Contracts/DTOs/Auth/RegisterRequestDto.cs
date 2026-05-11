namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>注册新用户请求 DTO（需要管理员权限）</summary>
    public class RegisterRequestDto
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
        public string NickName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
    }
}
