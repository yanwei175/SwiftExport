using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using SwiftExport.Application.Interfaces.Auth.Session;
using SwiftExport.Application.Interfaces.Auth.Users;
using SwiftExport.Contracts.DTOs.Auth;

using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.WebApi.Controllers.Auth;

/// <summary>公开登录、当前身份及受保护的账号和密码管理 HTTP 边界。</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthSessionService authService,
    IUserAccountService accountService,
    IAuthTokenFactory tokenFactory,
    IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>验证用户名和密码并签发原十二小时 JWT；显式 Web 模式先验证匿名防伪令牌并保存同一 JWT。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var browser = Request.Headers.TryGetValue(BrowserSessionProtocol.WebModeHeader, out var mode);
        if (browser)
        {
            if (mode.Count != 1 || mode[0] != "1")
                return ServiceErrorResponse.Create(ServiceErrorCodes.Validation, "浏览器登录标记无效。", BrowserSessionProtocol.WebModeHeader);
            var invalid = await ValidateBrowserCsrfAsync();
            if (invalid is not null) return invalid;
        }
        var result = await authService.AuthenticateAsync(request, cancellationToken);
        if (!result.Success) return ToActionResult(result);
        var token = tokenFactory.Create(result.Data);
        if (browser) BrowserSessionProtocol.StoreIssuedToken(Response, token.Access_token);
        return Ok(token);
    }

    /// <summary>匿名生成浏览器登录和退出的防伪请求令牌；调用不得携带 Bearer，也不接纳旧 Cookie 身份。</summary>
    [HttpGet("browser-session/csrf")]
    [AllowAnonymous]
    public IActionResult BrowserSessionCsrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new BrowserSessionCsrfDto { RequestToken = tokens.RequestToken! });
    }

    /// <summary>只读返回已经由现有 JWT 和凭据版本验证器确认有效的原 Cookie 令牌；不续签、不设置 Cookie。</summary>
    [HttpGet("browser-session/restore")]
    [Authorize]
    public IActionResult RestoreBrowserSession()
    {
        if (!Request.Cookies.TryGetValue(BrowserSessionProtocol.RestoreCookie, out var token) || string.IsNullOrWhiteSpace(token))
            return ServiceErrorResponse.Create(ServiceErrorCodes.Unauthenticated, "没有可恢复的浏览器登录状态。");
        return Ok(new LoginTokenDto { Access_token = token });
    }

    /// <summary>匿名防伪验证后删除浏览器恢复 Cookie；不撤销 JWT，不创建服务器会话，204 仅表示退出协议已执行。</summary>
    [HttpPost("browser-session/logout")]
    [AllowAnonymous]
    public async Task<IActionResult> LogoutBrowserSession()
    {
        var invalid = await ValidateBrowserCsrfAsync();
        if (invalid is not null) return invalid;
        BrowserSessionProtocol.DeleteRestoreCookie(Response);
        return NoContent();
    }

    /// <summary>返回当前活动用户、角色和稳定权限键。</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserAsync(cancellationToken);
        return result.Success ? Ok(result.Data) : ToActionResult(result);
    }

    /// <summary>仅由当前有效 Admin 创建无角色的新用户。</summary>
    [HttpPost("register")]
    [Authorize]
    [PreventDuplicateRequest]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var result = await accountService.RegisterAsync(request, cancellationToken);
        return result.Success ? Ok(result) : ToActionResult(result);
    }

    /// <summary>当前用户验证旧密码后修改本人密码。</summary>
    [HttpPost("change-password")]
    [Authorize]
    [PreventDuplicateRequest]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request, CancellationToken cancellationToken)
    {
        var result = await accountService.ChangeOwnPasswordAsync(request, cancellationToken);
        return result.Success ? Ok(result) : ToActionResult(result);
    }

    /// <summary>仅由当前有效 Admin 重置非 Admin 用户密码。</summary>
    [HttpPost("admin/reset-password")]
    [Authorize]
    [PreventDuplicateRequest]
    public async Task<IActionResult> ResetUserPassword([FromBody] AdminResetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        var result = await accountService.ResetUserPasswordAsync(request, cancellationToken);
        return result.Success ? Ok(result) : ToActionResult(result);
    }

    /// <summary>仅由当前有效 Admin 获取权限维护目标的最小用户列表。</summary>
    [HttpGet("admin/users")]
    [Authorize]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var result = await accountService.GetUsersAsync(cancellationToken);
        return result.Success ? Ok(result) : ToActionResult(result);
    }

    /// <summary>转交全局结果过滤器处理失败代码与 HTTP 状态。</summary>
    private IActionResult ToActionResult<T>(ServiceResult<T> result) => new ObjectResult(result);

    /// <summary>Web 登录和退出始终以匿名身份验证同一套 ASP.NET 防伪令牌；失败不调用认证服务或删除 Cookie。</summary>
    private async Task<IActionResult?> ValidateBrowserCsrfAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return ServiceErrorResponse.Create(ServiceErrorCodes.Validation, "浏览器防伪验证失败，请重新获取防伪令牌。", BrowserSessionProtocol.CsrfHeader);
        }
    }
}
