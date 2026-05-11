using SwiftExport.Core.Interfaces.Auth;
using System.Security.Claims;

namespace SwiftExport.WebApi.Services;

/// <summary>
/// 从 HttpContext Claims 中读取当前用户信息。
/// JWT 登录用户和 DevUser 中间件注入的用户均通过此处统一访问。
/// </summary>
public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <summary>是否已认证（JWT 或 Dev 用户均为 true）</summary>
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <summary>用户名，取 unique_name claim；未认证则返回 "system"</summary>
    public string UserName =>
        Principal?.FindFirstValue(ClaimTypes.Name)
        ?? Principal?.FindFirstValue("unique_name")
        ?? "system";

    /// <summary>用户 ID，取 sub claim；未认证返回 null</summary>
    public int? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? Principal?.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : null;
        }
    }
}
