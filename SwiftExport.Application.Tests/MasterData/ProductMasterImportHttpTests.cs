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
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Products.ProductMasters;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>真实 MVC、Refit、认证及权限管道贯通父级导入导出；仓储使用内存事务，不运行生产启动入口。</summary>
[TestClass]
public sealed class ProductMasterImportHttpTests
{
    /// <summary>匿名拒绝；普通查看可预览和导出，普通管理员键持有者仍无法写入，活动管理员可原子提交。</summary>
    [TestMethod]
    public async Task HttpAndRefit_EnforceAuthenticationAndLiveAdmin()
    {
        var fixture = new ProductMasterImportExportTests.Fixture { Admin = false };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ICurrentUser>(fixture);
        builder.Services.AddSingleton<IPermissionEvaluationService>(fixture);
        builder.Services.AddSingleton<IProductMasterImportService>(fixture.Service);
        builder.Services.AddSingleton<IProductMasterQueryService>(fixture.Query);
        builder.Services.AddMemoryCache(); builder.Services.AddScoped<EndpointPermissionFilter>();
        builder.Services.AddAuthentication("ImportTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("ImportTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<EndpointPermissionFilter>(); options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(ProductMasterImportExportController).Assembly).AddNewtonsoftJson();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address + "/api/ProductMasterImportExport") };
        var inputs = new List<ProductMasterImportRowInput> { new() { SourceRowNumber = 2, MasterSku = "M-1", DefaultDescriptionZh = "HTTP 导入" } };
        using var anonymous = await client.PostAsJsonAsync("/api/ProductMasterImportExport/preview", new ProductMasterImportPreviewRequest { Rows = inputs });
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, fixture.Reads);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "isolated-import-token");
        var api = Refit.RestService.For<IProductMasterImportExportApi>(client);
        var preview = await api.PreviewAsync(new() { Rows = inputs });
        Assert.IsTrue(preview.Success, preview.Message);
        Assert.AreEqual("Update", preview.Data.Rows[0].Operation);
        Assert.AreEqual(1, (await api.ExportAsync(new())).Data.Count);
        var commit = ProductMasterImportExportTests.Commit(inputs, preview.Data);
        using var ordinaryWrite = await client.PostAsJsonAsync("/api/ProductMasterImportExport/import", commit);
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinaryWrite.StatusCode);
        Assert.AreEqual(0, fixture.Writes);
        // 防重复提交本身不决定业务授权；清理本测试产生的冷却标记后再次检查实时管理员准入。
        app.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            .Remove($"prevent_dup:{fixture.UserId}:ProductMasterImportExport:Import");
        fixture.Admin = true;
        var saved = await api.ImportAsync(commit);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual("HTTP 导入", fixture.Masters[0].DefaultDescriptionZh);
        Assert.AreEqual(1, fixture.Writes);
        fixture.Admin = false; fixture.Active = false;
        using var inactive = await client.PostAsJsonAsync("/api/ProductMasterImportExport/export", new ProductMasterExportRequest());
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

