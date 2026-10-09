using System.IdentityModel.Tokens.Jwt;

namespace SwiftExport.WebApi.Authentication;

/// <summary>限定浏览器原 JWT 恢复协议的路径、标记和 Cookie；不创建服务端会话或延长 JWT 有效期。</summary>
public static class BrowserSessionProtocol
{
    /// <summary>浏览器恢复、匿名防伪和退出端点共同使用的前缀，也是恢复 Cookie 的唯一作用路径。</summary>
    public const string Prefix = "/api/auth/browser-session";
    /// <summary>只读恢复原 JWT 的路径；其他 API 不能从 Cookie 建立业务身份。</summary>
    public const string RestorePath = Prefix + "/restore";
    /// <summary>显式选择浏览器登录协议的请求头；未携带该标记的 WPF 登录保持原契约。</summary>
    public const string WebModeHeader = "X-SwiftExport-Web-Session";
    /// <summary>与匿名防伪 Cookie 配对的请求令牌头。</summary>
    public const string CsrfHeader = "X-CSRF-TOKEN";
    /// <summary>保存同一次登录原 JWT 的 HttpOnly Cookie 名称。</summary>
    public const string RestoreCookie = "SwiftExport.Web.Restore";
    /// <summary>仅用于浏览器登录/退出防伪验证的 HttpOnly Cookie 名称。</summary>
    public const string CsrfCookie = "SwiftExport.Web.Csrf";

    /// <summary>识别浏览器协议请求，包括带有标记但值不合法的登录，避免静默退回 WPF 登录路径。</summary>
    public static bool IsBrowserRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase)
        || IsLoginPath(request) && request.Headers.ContainsKey(WebModeHeader);

    /// <summary>按 MVC 路由的大小写和末尾斜线语义识别唯一恢复端点。</summary>
    public static bool IsRestoreRequest(HttpRequest request) =>
        string.Equals(request.Path.Value?.TrimEnd('/'), RestorePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>识别既有登录路径；仅携带明确 Web 标记时才应用浏览器协议。</summary>
    public static bool IsLoginPath(HttpRequest request) =>
        string.Equals(request.Path.Value?.TrimEnd('/'), "/api/auth/login", StringComparison.OrdinalIgnoreCase);

    /// <summary>保存由现有签发器刚生成的原 JWT；只读取其到期时间，不重复签发或自行验证请求中的 JWT。</summary>
    public static void StoreIssuedToken(HttpResponse response, string issuedToken)
    {
        var expires = new JwtSecurityTokenHandler().ReadJwtToken(issuedToken).ValidTo;
        if (expires <= DateTime.UtcNow) throw new InvalidOperationException("不能保存已过期的登录令牌。");
        var options = RestoreCookieOptions();
        options.Expires = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc));
        response.Cookies.Append(RestoreCookie, issuedToken, options);
    }

    /// <summary>按原名称、host-only 域及路径删除恢复 Cookie；成功响应不宣称已撤销其他客户端的 JWT。</summary>
    public static void DeleteRestoreCookie(HttpResponse response) =>
        response.Cookies.Delete(RestoreCookie, RestoreCookieOptions());

    /// <summary>恢复 Cookie 仅发送到浏览器会话前缀，必须 HTTPS、HttpOnly、同站严格；不设置 Domain。</summary>
    private static CookieOptions RestoreCookieOptions() => new()
    {
        Path = Prefix,
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true
    };
}
