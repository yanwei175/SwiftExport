using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Files;
using SwiftExport.Application.Services.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Files;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.WebApi.Files;

namespace SwiftExport.Application.Tests.Files;

/// <summary>通过真实 Kestrel、multipart reader、Skia 和临时目录验证产品图片上传 HTTP 边界。</summary>
[TestClass]
public sealed class ProductImageApiIntegrationTests
{
    /// <summary>匿名、活动普通用户和失效账号均不能写入；实时 Admin 可部分成功并公开两张 JPEG。</summary>
    [TestMethod]
    public async Task PermissionsAndBatchResults_ShouldPreserveZeroWriteAndPublicRead()
    {
        await using var deniedHost = await ApiHost.StartAsync(active: true, isAdmin: false);
        using var anonymous = deniedHost.CreateClient(authenticated: false);
        using var anonymousResponse = await anonymous.PostAsync(
            "/api/product-images/batch",
            CreateMultipart(("a.png", CreatePng(8, 8))));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var normal = deniedHost.CreateClient(authenticated: true);
        using var forbidden = await normal.PostAsync(
            "/api/product-images/batch",
            CreateMultipart(("a.png", CreatePng(8, 8))));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(deniedHost.ImageDirectory).Any());
        Assert.IsFalse(Directory.Exists(Path.Combine(deniedHost.Root, ".product-image-state")));

        await using var inactiveHost = await ApiHost.StartAsync(active: false, isAdmin: true);
        using var inactive = inactiveHost.CreateClient(authenticated: true);
        using var inactiveResponse = await inactive.PostAsync(
            "/api/product-images/batch",
            CreateMultipart(("a.png", CreatePng(8, 8))));
        Assert.AreEqual(HttpStatusCode.Unauthorized, inactiveResponse.StatusCode);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(inactiveHost.ImageDirectory).Any());

        await using var adminHost = await ApiHost.StartAsync(active: true, isAdmin: true);
        using var admin = adminHost.CreateClient(authenticated: true);
        using var response = await admin.PostAsync(
            "/api/product-images/batch",
            CreateMultipart(
                ("中文 图.png", CreatePng(40, 20)),
                ("中文 图.jpg", CreateJpeg(2, 2)),
                ("broken.webp", [1, 2, 3])));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ServiceResult<ProductImageBatchUploadResultDto>>();
        Assert.IsNotNull(envelope);
        Assert.IsTrue(envelope.Success);
        Assert.AreEqual(3, envelope.Data.Total);
        Assert.AreEqual(1, envelope.Data.Succeeded);
        Assert.AreEqual(2, envelope.Data.Failed);
        Assert.AreEqual(ProductImageErrorCodes.DuplicateTargetInBatch, envelope.Data.Items[1].ErrorCode);
        Assert.AreEqual(ProductImageErrorCodes.ProcessingFailed, envelope.Data.Items[2].ErrorCode);
        StringAssert.Contains(envelope.Data.Items[0].OriginalUrl, "/virtual/files/ProductImages/");

        using var publicClient = adminHost.CreateClient(authenticated: false);
        using var original = await publicClient.GetAsync("/files/ProductImages/" + Uri.EscapeDataString("中文 图.jpg"));
        Assert.AreEqual(HttpStatusCode.OK, original.StatusCode);
        Assert.AreEqual("image/jpeg", original.Content.Headers.ContentType!.MediaType);
        using var thumbnail = await publicClient.GetAsync("/files/ProductImages/Thumbnails/" + Uri.EscapeDataString("中文 图.jpg"));
        Assert.AreEqual(HttpStatusCode.OK, thumbnail.StatusCode);
        using var state = await publicClient.GetAsync(
            "/files/ProductImages/%2e%2e/.product-image-state/product-images/%E4%B8%AD%E6%96%87%20%E5%9B%BE.jpg.json");
        Assert.AreEqual(HttpStatusCode.NotFound, state.StatusCode);
    }

    /// <summary>文件数和包含 multipart 开销的总请求上限返回 413；已完成的前项不回滚。</summary>
    [TestMethod]
    public async Task RequestLimits_ShouldStopBatchWithoutRollingBackCompletedItems()
    {
        await using var fileCountHost = await ApiHost.StartAsync(active: true, isAdmin: true, maxFiles: 1);
        using var client = fileCountHost.CreateClient(authenticated: true);
        using var tooMany = await client.PostAsync(
            "/api/product-images/batch",
            CreateMultipart(("first.png", CreatePng(2, 2)), ("second.png", CreatePng(2, 2))));
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, tooMany.StatusCode);
        Assert.IsTrue(File.Exists(Path.Combine(fileCountHost.ImageDirectory, "first.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(fileCountHost.ImageDirectory, "second.jpg")));

        await using var bytesHost = await ApiHost.StartAsync(
            active: true,
            isAdmin: true,
            maxBatchBytes: 256,
            maxFileBytes: 128);
        using var bytesClient = bytesHost.CreateClient(authenticated: true);
        using var tooLarge = await bytesClient.PostAsync(
            "/api/product-images/batch",
            CreateStreamingMultipart("large.png", new byte[512]));
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(bytesHost.ImageDirectory, "large.jpg")));

        await using var fileBytesHost = await ApiHost.StartAsync(
            active: true,
            isAdmin: true,
            maxFileBytes: 128);
        using var fileBytesClient = fileBytesHost.CreateClient(authenticated: true);
        using var perFile = await fileBytesClient.PostAsync(
            "/api/product-images/batch",
            CreateStreamingMultipart("large.png", new byte[256]));
        Assert.AreEqual(HttpStatusCode.OK, perFile.StatusCode);
        var result = await perFile.Content.ReadFromJsonAsync<ServiceResult<ProductImageBatchUploadResultDto>>();
        Assert.AreEqual(ProductImageErrorCodes.FileTooLarge, result!.Data.Items.Single().ErrorCode);
        Assert.IsFalse(File.Exists(Path.Combine(fileBytesHost.ImageDirectory, "large.jpg")));
    }

    private static MultipartFormDataContent CreateMultipart(params (string Name, byte[] Bytes)[] files)
    {
        var content = new MultipartFormDataContent("----SwiftExportProductImageBoundary");
        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "files", file.Name);
        }

        return content;
    }

    private static MultipartFormDataContent CreateStreamingMultipart(string name, byte[] bytes)
    {
        var content = new MultipartFormDataContent("----SwiftExportProductImageStreamingBoundary");
        var part = new StreamContent(new NonSeekReadStream(bytes));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "files", name);
        return content;
    }

    private static byte[] CreatePng(int width, int height) => CreateImage(width, height, SKEncodedImageFormat.Png);
    private static byte[] CreateJpeg(int width, int height) => CreateImage(width, height, SKEncodedImageFormat.Jpeg);

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>建立独立临时目录、真实静态入口和测试认证投影，不连接业务数据库。</summary>
    private sealed class ApiHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ApiHost(WebApplication app, string root, string address)
        {
            _app = app;
            Root = root;
            Address = address;
        }

        public string Root { get; }
        public string ImageDirectory => Path.Combine(Root, "ProductImages");
        private string Address { get; }

        public static async Task<ApiHost> StartAsync(
            bool active,
            bool isAdmin,
            int maxFiles = 20,
            long maxBatchBytes = 2 * 1024 * 1024,
            long maxFileBytes = 1024 * 1024)
        {
            var root = Path.Combine(Path.GetTempPath(), "SwiftExport-product-image-api-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "ProductImages"));
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Enabled"] = "true",
                ["FileStorage:RootPath"] = root,
                ["FileStorage:ApiBaseUrl"] = "http://127.0.0.1:51234/virtual/",
                ["FileStorage:ProductImages:UploadEnabled"] = "true",
                ["FileStorage:ProductImages:MaxFileBytes"] = maxFileBytes.ToString(),
                ["FileStorage:ProductImages:MaxBatchBytes"] = maxBatchBytes.ToString(),
                ["FileStorage:ProductImages:MaxFiles"] = maxFiles.ToString(),
                ["FileStorage:ProductImages:MaxPixels"] = "1000000",
                ["FileStorage:ProductImages:MaxConcurrentProcessing"] = "2"
            });
            builder.Services.AddConfiguredFileStorage(builder.Configuration);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddSingleton<IPermissionEvaluationService>(new PermissionProjectionStub(active, isAdmin));
            builder.Services.AddScoped<IFileAccessPolicy, FileAccessPolicy>();
            builder.Services.AddSingleton<ProductImageTargetLockManager>();
            builder.Services.AddScoped<IProductImageUploadService, ProductImageUploadService>();
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers(options => options.Filters.Add<ServiceResultHttpFilter>())
                .AddApplicationPart(typeof(ProductImageController).Assembly)
                .AddNewtonsoftJson();
            var app = builder.Build();
            app.UseMiddleware<GlobalExceptionMiddleware>();
            app.UseProductImages();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ApiHost(app, root, address);
        }

        public HttpClient CreateClient(bool authenticated)
        {
            var client = new HttpClient { BaseAddress = new Uri(Address) };
            if (authenticated)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class PermissionProjectionStub(bool active, bool isAdmin) : IPermissionEvaluationService
    {
        public Task<UserPermissionProjection?> GetProjectionAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(active ? new UserPermissionProjection { IsAdmin = isAdmin } : null);

        public Task<bool> HasPermissionAsync(
            Guid userId,
            string permissionKey,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>模拟无法预先计算 Content-Length 的客户端文件流。</summary>
    private sealed class NonSeekReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>只有显式测试 Authorization 头建立身份，保留匿名 401 场景。</summary>
    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ProductImageApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
