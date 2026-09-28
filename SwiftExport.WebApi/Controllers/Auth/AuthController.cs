using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth.Session;
using SwiftExport.Application.Interfaces.Auth.Users;
using SwiftExport.Contracts.DTOs.Auth;

using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;
using SwiftExport.WebApi.Authentication;

namespace SwiftExport.WebApi.Controllers.Auth;

/// <summary>公开登录、当前身份及受保护的账号和密码管理 HTTP 边界。</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthSessionService authService,
    IUserAccountService accountService,
    IAuthTokenFactory tokenFactory) : ControllerBase
{
    /// <summary>验证用户名和密码并签发带凭据版本的 JWT。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.AuthenticateAsync(request, cancellationToken);
        return result.Success ? Ok(tokenFactory.Create(result.Data)) : ToActionResult(result);
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
}
