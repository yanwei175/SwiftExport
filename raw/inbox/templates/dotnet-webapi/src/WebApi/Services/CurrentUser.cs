using System.Security.Claims;
using {{ProjectName}}.Core.Interfaces.Auth;

namespace {{ProjectName}}.WebApi.Services;

public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public string UserName => Principal?.FindFirstValue(ClaimTypes.Name) ?? Principal?.FindFirstValue("unique_name") ?? "system";

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(value, out var uid) ? uid : null;
        }
    }
}
