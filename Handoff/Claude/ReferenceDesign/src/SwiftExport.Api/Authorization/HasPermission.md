# 授权：HasPermissionAttribute / PermissionPolicyProvider / PermissionHandler

```csharp
namespace SwiftExport.Api.Authorization;

// 用法：[HasPermission(Permissions.PurchaseOrders.Confirm)]
public sealed class HasPermissionAttribute(string permission)
	: AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission);

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
	public string Permission { get; } = permission;
}

// 根据 Policy 名称动态生成策略，不需要逐个注册
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
	: DefaultAuthorizationPolicyProvider(options)
{
	public const string Prefix = "perm:";
	public override Task<AuthorizationPolicy?> GetPolicyAsync(string name) =>
		name.StartsWith(Prefix)
			? Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
				.RequireAuthenticatedUser()
				.AddRequirements(new PermissionRequirement(name[Prefix.Length..])).Build())
			: base.GetPolicyAsync(name);
}

// 复用现有角色-权限表；缓存 5 分钟，角色权限修改时调用 cache.Remove($"perm:{userId}")
public sealed class PermissionHandler(IPermissionReader reader, IMemoryCache cache)
	: AuthorizationHandler<PermissionRequirement>
{
	protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, PermissionRequirement req)
	{
		var userId = ctx.User.GetUserId();
		var granted = await cache.GetOrCreateAsync($"perm:{userId}", e =>
		{
			e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
			return reader.GetPermissionsAsync(userId);   // HashSet<string>
		});
		if (granted!.Contains(req.Permission)) ctx.Succeed(req);
	}
}
```

防漏配测试（`SwiftExport.Api.Tests/EndpointPermissionTests`）：反射所有 Controller Action，要求有 `HasPermissionAttribute` 或 `AllowAnonymousAttribute`，否则失败。
