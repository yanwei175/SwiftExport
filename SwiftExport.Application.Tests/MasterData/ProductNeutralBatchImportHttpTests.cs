using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Import;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Query;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Products.ProductNeutrals;
using SwiftExport.WebApi.ErrorHandling;
using static SwiftExport.Application.Tests.MasterData.ProductMasterBatchImportTests;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>真实 MVC、Refit 和权限管道验证中性导入；业务仓储使用内存行事务。</summary>
[TestClass]
public sealed class ProductNeutralBatchImportHttpTests
{
    /// <summary>匿名和普通编辑用户拒绝导入，管理员可连续提交，失效账号再次被拒绝。</summary>
    [TestMethod]
    public async Task HttpAndRefitRequireLiveAdminWithoutBatchCooldown()
    {
        var fixture = new ProductNeutralBatchImportTests.Fixture { Admin = false };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ICurrentUser>(fixture);
        builder.Services.AddSingleton<IPermissionEvaluationService>(fixture);
        builder.Services.AddSingleton<IProductNeutralImportService>(fixture.Service);
        builder.Services.AddSingleton(Proxy<IProductNeutralQueryService>((name, _) => throw new AssertFailedException(name)));
        builder.Services.AddSingleton(Proxy<IProductNeutralBatchMaintenanceService>((name, _) => throw new AssertFailedException(name)));
        builder.Services.AddMemoryCache(); builder.Services.AddScoped<EndpointPermissionFilter>();
        builder.Services.AddAuthentication("ImportTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("ImportTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
        { options.Filters.Add<EndpointPermissionFilter>(); options.Filters.Add<ServiceResultHttpFilter>(); })
            .AddApplicationPart(typeof(ProductNeutralManagerController).Assembly).AddNewtonsoftJson();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address + "/api/ProductNeutralManager") };
        var rows = new List<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import.ProductNeutralImportRowDto> { fixture.Row("NEW") };
        using var anonymous = await client.PostAsJsonAsync("/api/ProductNeutralManager/import", rows);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode); Assert.AreEqual(0, fixture.Reads);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "isolated-neutral-import-token");
        using var ordinary = await client.PostAsJsonAsync("/api/ProductNeutralManager/import", rows);
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinary.StatusCode); Assert.AreEqual(0, fixture.Writes);
        fixture.Admin = true;
        var api = Refit.RestService.For<IProductNeutralManagerApi>(client);
        Assert.IsTrue((await api.ImportAsync(rows)).Data.Items.Single().Success);
        Assert.IsTrue((await api.ImportAsync(rows)).Data.Items.Single().Success);
        Assert.AreEqual(2, fixture.Writes);
        using var invalid = await client.PostAsJsonAsync("/api/ProductNeutralManager/import", Array.Empty<object>());
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        fixture.Active = false;
        using var inactive = await client.PostAsJsonAsync("/api/ProductNeutralManager/import", rows);
        Assert.AreEqual(HttpStatusCode.Forbidden, inactive.StatusCode); Assert.AreEqual(2, fixture.Writes);
    }

    /// <summary>隔离认证只识别固定测试令牌，不访问实际用户会话。</summary>
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer isolated-neutral-import-token") return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
