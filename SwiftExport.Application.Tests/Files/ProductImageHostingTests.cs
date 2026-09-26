using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.WebApi.Files;

namespace SwiftExport.Application.Tests.Files;

/// <summary>使用临时目录与独立 HTTP 宿主验证图片读取，不加载业务服务或连接数据库。</summary>
[TestClass]
public sealed class ProductImageHostingTests
{
    /// <summary>未启用不要求目录；显式启用时拒绝缺失或相对根目录，避免依赖程序工作目录。</summary>
    [TestMethod]
    public async Task Configuration_IsOptInAndRejectsRelativeRoot()
    {
        var disabledBuilder = WebApplication.CreateBuilder();
        disabledBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:Enabled"] = "false"
        });
        disabledBuilder.Services.AddConfiguredFileStorage(disabledBuilder.Configuration);
        await using var disabled = disabledBuilder.Build();
        disabled.UseProductImages();
        foreach (var rootPath in new string?[] { null, "relative-images" })
        {
            var enabledBuilder = WebApplication.CreateBuilder();
            enabledBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Enabled"] = "true", ["FileStorage:RootPath"] = rootPath
            });
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                enabledBuilder.Services.AddConfiguredFileStorage(enabledBuilder.Configuration));
        }
    }

    /// <summary>验证读取、缓存条件、Range、同路径替换及目录隔离。</summary>
    [TestMethod]
    public async Task Images_AreReadOnlyScopedAndRevalidated()
    {
        var root = Path.Combine(Path.GetTempPath(), "SwiftExport-image-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ProductImages"));
        Directory.CreateDirectory(Path.Combine(root, "AfterSales"));
        var path = Path.Combine(root, "ProductImages", "TEST.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(root, "AfterSales", "private.jpg"), "private");
        await File.WriteAllTextAsync(Path.Combine(root, "ProductImages", "private.txt"), "private");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:Enabled"] = "true", ["FileStorage:RootPath"] = root
        });
        builder.Services.AddConfiguredFileStorage(builder.Configuration);
        await using var app = builder.Build();
        app.UseProductImages();
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await client.GetAsync("/files/ProductImages/TEST.jpg");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, await response.Content.ReadAsByteArrayAsync());
            Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType!.MediaType);
            Assert.IsTrue(response.Headers.CacheControl!.NoCache);
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/files/ProductImages/TEST.jpg");
            using var head = await client.SendAsync(headRequest);
            Assert.AreEqual(HttpStatusCode.OK, head.StatusCode);
            Assert.AreEqual(4L, head.Content.Headers.ContentLength);
            Assert.AreEqual(0, (await head.Content.ReadAsByteArrayAsync()).Length);
            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/files/ProductImages/TEST.jpg");
            conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var unchanged = await client.SendAsync(conditional);
            Assert.AreEqual(HttpStatusCode.NotModified, unchanged.StatusCode);
            using var range = new HttpRequestMessage(HttpMethod.Get, "/files/ProductImages/TEST.jpg");
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
            using var part = await client.SendAsync(range);
            Assert.AreEqual(HttpStatusCode.PartialContent, part.StatusCode);
            CollectionAssert.AreEqual(new byte[] { 2, 3 }, await part.Content.ReadAsByteArrayAsync());
            await File.WriteAllBytesAsync(path, [5, 6, 7, 8, 9]);
            using var changedRequest = new HttpRequestMessage(HttpMethod.Get, "/files/ProductImages/TEST.jpg");
            changedRequest.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var changed = await client.SendAsync(changedRequest);
            Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8, 9 }, await changed.Content.ReadAsByteArrayAsync());
            foreach (var url in new[] { "/files/ProductImages/", "/files/ProductImages/missing.jpg", "/files/AfterSales/private.jpg", "/files/ProductImages/private.txt", "/files/ProductImages/%2e%2e/AfterSales/private.jpg" })
            {
                using var denied = await client.GetAsync(url);
                Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode, url);
            }
            using var post = await client.PostAsync("/files/ProductImages/TEST.jpg", new StringContent("replace"));
            Assert.AreEqual(HttpStatusCode.NotFound, post.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            // 只清理本测试创建的随机临时目录，不接触业务存储目录。
            Directory.Delete(root, true);
        }
    }
}
