namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>
    /// 重置密码结果 DTO。
    /// </summary>
    public class ResetPasswordResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
