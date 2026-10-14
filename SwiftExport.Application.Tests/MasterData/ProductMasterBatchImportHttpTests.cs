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
using SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Products.ProductMasters;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>真实 MVC、Refit、认证及权限管道贯通连续父级导入批次；仓储使用内存事务，不运行生产启动入口。</summary>
[TestClass]
public sealed class ProductMasterBatchImportHttpTests
{
    /// <summary>匿名拒绝，普通编辑权限持有者无法导入，活动管理员可连续提交。</summary>
    [TestMethod]
    public async Task HttpAndRefit_EnforceAuthenticationAndLiveAdmin()
    {
        var fixture = new ProductMasterBatchImportTests.Fixture { Admin = false };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ICurrentUser>(fixture);
        builder.Services.AddSingleton<IPermissionEvaluationService>(fixture);
        builder.Services.AddSingleton<IProductMasterImportService>(fixture.Service);
        builder.Services.AddMemoryCache(); builder.Services.AddScoped<EndpointPermissionFilter>();
        builder.Services.AddAuthentication("ImportTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("ImportTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<EndpointPermissionFilter>(); options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(ProductMasterImportController).Assembly).AddNewtonsoftJson();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address + "/api/ProductMasterImport") };
        var inputs = new List<ProductMasterImportRowDto> { new() { SourceRowNumber = 2, MasterSku = "M-1", DefaultDescriptionZh = "HTTP 导入", ProductSeriesNameEn = " standard " } };
        using var anonymous = await client.PostAsJsonAsync("/api/ProductMasterImport/import", inputs);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, fixture.Reads);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "isolated-import-token");
        using var ordinaryWrite = await client.PostAsJsonAsync("/api/ProductMasterImport/import", inputs);
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinaryWrite.StatusCode);
        Assert.AreEqual(0, fixture.Writes);
        fixture.Admin = true;
        var api = Refit.RestService.For<IProductMasterImportApi>(client);
        var saved = await api.ImportAsync(inputs);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.IsTrue(saved.Data.Items.Single().Success);
        Assert.AreEqual("HTTP 导入", fixture.Masters[0].DefaultDescriptionZh);
        Assert.AreEqual(fixture.TargetSeries.Uid, fixture.Masters[0].ProductSeriesId);
        // 连续批次不需要冷却或清理缓存，第二次也立即到达应用服务。
        Assert.IsTrue((await api.ImportAsync(inputs)).Data.Items.Single().Success);
        Assert.AreEqual(2, fixture.Writes);
        fixture.Active = false;
        using var inactive = await client.PostAsJsonAsync("/api/ProductMasterImport/import", inputs);
        Assert.AreEqual(HttpStatusCode.Forbidden, inactive.StatusCode);

    }

    /// <summary>只认可本测试的 Bearer，保持匿名端点使用正常认证挑战。</summary>
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer isolated-import-token") return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}


