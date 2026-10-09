namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>仅供浏览器登录和本地会话退出使用的匿名防伪请求令牌；不包含账号身份或访问令牌。</summary>
    public sealed class BrowserSessionCsrfDto
    {
        /// <summary>浏览器通过 X-CSRF-TOKEN 请求头回传，与 HttpOnly 防伪 Cookie 配对验证。</summary>
        public string RequestToken { get; set; }
    }
}
