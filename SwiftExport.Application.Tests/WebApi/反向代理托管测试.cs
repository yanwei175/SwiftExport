using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.WebApi.Configuration;

namespace SwiftExport.Application.Tests.WebApi;

/// <summary>以独立 Kestrel 和生产管道验证同机 TLS 代理；不解析仓储、不连接数据库。</summary>
[TestClass]
public sealed class ReverseProxyHostingTests
{
    /// <summary>回环代理恢复 HTTPS 与客户端 IP，避免循环跳转；局域网直连的伪造头必须被忽略。</summary>
    [TestMethod]
    [DataRow("127.0.0.1", true)]
    [DataRow("::1", true)]
    [DataRow("192.0.2.10", false)]
    public async Task ProxyHeaders_AreTrustedOnlyFromLoopback(string peer, bool trusted)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:JwtKey"] = "proxy-test-only-at-least-32-byte-signing-key",
            ["Auth:JwtIssuer"] = "proxy-test", ["Auth:JwtAudience"] = "proxy-test",
            ["Auth:UseDevUser"] = "false", ["FileStorage:Enabled"] = "false"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddWebApiServices();
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
        await using var app = builder.Build();
        // 控制传输对端，保留真实生产代理选项和其余 HTTP 管道。
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            return next(context);
        });
        app.UseWebApiPipeline();
        app.MapGet("/proxy-probe", (Microsoft.AspNetCore.Http.HttpContext context) =>
            context.Request.Scheme + "|" + context.Connection.RemoteIpAddress).AllowAnonymous();
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/proxy-probe");
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-For", "192.0.2.20");
            using var response = await client.SendAsync(request);
            if (trusted)
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual("https|192.0.2.20", await response.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
                Assert.AreEqual("https", response.Headers.Location!.Scheme);
            }
        }
        finally { await app.StopAsync(); }
    }
}
