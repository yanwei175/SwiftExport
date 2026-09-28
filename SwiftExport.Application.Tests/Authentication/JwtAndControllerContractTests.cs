using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Session;
using SwiftExport.Application.Models.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.RequestProtection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证 JWT 凭据版本声明、回查和认证 Controller 暴露边界。</summary>
[TestClass]
public sealed class JwtAndControllerContractTests
{
    /// <summary>签发令牌包含版本声明，校验器接受当前版本并拒绝旧版本。</summary>
    [TestMethod]
    public async Task TokenCarriesCredentialVersionAndValidatorRejectsOldVersion()
    {
        var userId = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:JwtKey"] = "unit-test-key-that-is-long-enough-for-hmac-sha256",
            ["Auth:JwtIssuer"] = "tests",
            ["Auth:JwtAudience"] = "tests"
        }).Build();
        var token = new AuthTokenFactory(configuration).Create(new AuthenticatedUser
        {
            UserId = userId, UserName = "tester", CredentialVersion = 7
        });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Access_token);
        Assert.AreEqual("7", jwt.Claims.Single(x => x.Type == AuthTokenClaims.CredentialVersion).Value);

        var auth = new AuthServiceStub(userId, 7);
        var validator = new CredentialVersionTokenValidator(auth);
        Assert.IsTrue(await validator.ValidateAsync(Principal(userId, 7)));
        auth.CurrentVersion = 8;
        Assert.IsFalse(await validator.ValidateAsync(Principal(userId, 7)));
        Assert.IsFalse(await validator.ValidateAsync(PrincipalWithoutVersion(userId)));
    }

    /// <summary>除登录外所有账号及密码写入口都要求授权，旧匿名路由不再存在。</summary>
    [TestMethod]
    public void ControllerHasNoAnonymousResetPasswordEndpoint()
    {
        var methods = typeof(AuthController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AuthController)).ToArray();
        var postRoutes = methods.SelectMany(method => method.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Select(attribute => (Method: method, Template: attribute.Template))).ToArray();
        Assert.IsFalse(postRoutes.Any(x => string.Equals(x.Template, "reset-password", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(postRoutes.Single(x => x.Template == "login").Method.IsDefined(typeof(AllowAnonymousAttribute), false));
        foreach (var route in postRoutes.Where(x => x.Template != "login"))
            Assert.IsTrue(route.Method.IsDefined(typeof(AuthorizeAttribute), false), route.Template);
    }

    /// <summary>旧模块管理端点已退出，账号列表仍要求授权。</summary>
    [TestMethod]
    public void ModuleManagementControllerEndpointsAreProtected()
    {
        var methods = typeof(AuthController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AuthController)).ToArray();
        var users = methods.Single(method => method.Name == nameof(AuthController.GetUsers));
        Assert.IsTrue(users.IsDefined(typeof(AuthorizeAttribute), false));
        Assert.AreEqual("admin/users", users.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template);
        Assert.IsFalse(methods.SelectMany(method => method.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>().Select(route => route.Template)).Any(route => route?.Contains("modules", StringComparison.OrdinalIgnoreCase) == true));
        Assert.IsFalse(methods.SelectMany(method => method.GetCustomAttributes(typeof(HttpPutAttribute), false)
            .Cast<HttpPutAttribute>().Select(route => route.Template)).Any(route => route?.Contains("modules", StringComparison.OrdinalIgnoreCase) == true));
    }

    private static ClaimsPrincipal Principal(Guid userId, int version) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, userId.ToString()), new(AuthTokenClaims.CredentialVersion, version.ToString())],
        "test"));

    private static ClaimsPrincipal PrincipalWithoutVersion(Guid userId) => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, userId.ToString())], "test"));

    private sealed class AuthServiceStub(Guid userId, int currentVersion) : IAuthSessionService
    {
        public int CurrentVersion { get; set; } = currentVersion;
        public Task<bool> IsCredentialVersionValidAsync(Guid id, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == userId && version == CurrentVersion);
        public Task<ServiceResult<AuthenticatedUser>> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<MeDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<bool>> ChangeOwnPasswordAsync(ChangePasswordRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<bool>> ResetUserPasswordAsync(AdminResetPasswordRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
