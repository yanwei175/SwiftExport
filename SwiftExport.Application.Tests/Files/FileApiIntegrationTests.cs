using System.Net;
using System.Net.Http.Headers;
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
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Files;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Files;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.WebApi.Files;

namespace SwiftExport.Application.Tests.Files;

/// <summary>使用真实 ASP.NET 管道验证原始流上传、GET/HEAD/Range 和受保护错误出口。</summary>
[TestClass]
public sealed class FileApiIntegrationTests
{
    /// <summary>Refit 以非 JSON 原始流上传，GET/HEAD/Range 返回受保护 attachment 且释放读取流。</summary>
    [TestMethod]
    public async Task AuthorizedApi_ShouldUploadDownloadHeadAndRange()
    {
        await using var host = await ApiHost.StartAsync(active: true, isAdmin: true, maxUploadBytes: 8);
        using var authenticatedClient = host.CreateClient(authenticated: true);
        var api = RestService.For<IFileApi>(authenticatedClient);
        using var source = new NonSeekReadStream([1, 2, 3, 4]);

        var upload = await api.UploadAsync("files", "中文 目录/a.bin", false, source);

        Assert.IsTrue(upload.Success);
        Assert.AreEqual("files/中文 目录/a.bin", upload.Data.Reference);
        Assert.AreEqual(
            "http://127.0.0.1:51234/virtual/api/files/files?path=%E4%B8%AD%E6%96%87%20%E7%9B%AE%E5%BD%95%2Fa.bin",
            upload.Data.ReadUrl);
        var requestPath = "/api/files/files?path=" + Uri.EscapeDataString("中文 目录/a.bin");
        using var get = await authenticatedClient.GetAsync(requestPath);
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        Assert.AreEqual("application/octet-stream", get.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", get.Content.Headers.ContentDisposition!.DispositionType);
        Assert.IsTrue(get.Headers.CacheControl!.Private);
        Assert.IsTrue(get.Headers.CacheControl.NoStore);
        Assert.AreEqual("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, await get.Content.ReadAsByteArrayAsync());

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, requestPath);
        using var head = await authenticatedClient.SendAsync(headRequest);
        Assert.AreEqual(HttpStatusCode.OK, head.StatusCode);
        Assert.AreEqual(4L, head.Content.Headers.ContentLength);
        Assert.AreEqual(0, (await head.Content.ReadAsByteArrayAsync()).Length);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 2);
        using var range = await authenticatedClient.SendAsync(rangeRequest);
        Assert.AreEqual(HttpStatusCode.PartialContent, range.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 2, 3 }, await range.Content.ReadAsByteArrayAsync());

        using var invalidRangeRequest = new HttpRequestMessage(HttpMethod.Get, requestPath);
        invalidRangeRequest.Headers.Range = new RangeHeaderValue(50, 60);
        using var invalidRange = await authenticatedClient.SendAsync(invalidRangeRequest);
        Assert.AreEqual(HttpStatusCode.RequestedRangeNotSatisfiable, invalidRange.StatusCode);

        // HEAD 和文件结果均应由框架释放打开流，随后可以独占打开正式文件。
        using var exclusive = new FileStream(
            Path.Combine(host.Root, "中文 目录", "a.bin"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    /// <summary>未认证、策略拒绝、冲突、媒体类型和无长度超限使用稳定状态且不发布文件。</summary>
    [TestMethod]
    public async Task ApiFailures_ShouldUseStableStatusAndPreserveStorage()
    {
        await using var normalUserHost = await ApiHost.StartAsync(active: true, isAdmin: false, maxUploadBytes: 4);
        using var anonymous = normalUserHost.CreateClient(authenticated: false);
        using var anonymousBody = new ByteArrayContent([1]);
        anonymousBody.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var unauthenticated = await anonymous.PutAsync("/api/files/files?path=a.bin", anonymousBody);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var normalUser = normalUserHost.CreateClient(authenticated: true);
        using var deniedBody = new ByteArrayContent([1]);
        deniedBody.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var forbidden = await normalUser.PutAsync("/api/files/files?path=a.bin", deniedBody);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(normalUserHost.Root, "a.bin")));
        await File.WriteAllBytesAsync(Path.Combine(normalUserHost.Root, "readable.bin"), [7, 8]);
        using var allowedRead = await normalUser.GetAsync("/api/files/files?path=readable.bin");
        Assert.AreEqual(HttpStatusCode.OK, allowedRead.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 7, 8 }, await allowedRead.Content.ReadAsByteArrayAsync());

        await using var inactiveHost = await ApiHost.StartAsync(active: false, isAdmin: true, maxUploadBytes: 4);
        using var inactive = inactiveHost.CreateClient(authenticated: true);
        using var inactiveBody = new ByteArrayContent([1]);
        inactiveBody.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var invalidAccountWrite = await inactive.PutAsync("/api/files/files?path=a.bin", inactiveBody);
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidAccountWrite.StatusCode);
        using var invalidAccountRead = await inactive.GetAsync("/api/files/files?path=a.bin");
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidAccountRead.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(inactiveHost.Root, "a.bin")));

        await using var host = await ApiHost.StartAsync(active: true, isAdmin: true, maxUploadBytes: 4);
        using var client = host.CreateClient(authenticated: true);
        using var wrongMedia = new StringContent("not-binary");
        using var unsupported = await client.PutAsync("/api/files/files?path=bad.bin", wrongMedia);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);

        using var invalidPathBody = new ByteArrayContent([1]);
        invalidPathBody.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var invalidPath = await client.PutAsync("/api/files/files?path=..%2Foutside.bin", invalidPathBody);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidPath.StatusCode);
        var invalidPathJson = await invalidPath.Content.ReadAsStringAsync();
        Assert.IsFalse(invalidPathJson.Contains(host.Root, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.Exists(Path.Combine(Directory.GetParent(host.Root)!.FullName, "outside.bin")));

        using var unknown = await client.GetAsync("/api/files/unknown?path=a.bin");
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        var api = RestService.For<IFileApi>(client);
        using var initial = new NonSeekReadStream([1]);
        Assert.IsTrue((await api.UploadAsync("files", "same.bin", false, initial)).Success);
        using var duplicate = new NonSeekReadStream([2]);
        var conflict = await Assert.ThrowsAsync<ApiException>(
            () => api.UploadAsync("files", "same.bin", false, duplicate));
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);

        using var tooLarge = new NonSeekReadStream([1, 2, 3, 4, 5]);
        var overflow = await Assert.ThrowsAsync<ApiException>(
            () => api.UploadAsync("files", "large.bin", false, tooLarge));
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, overflow.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(host.Root, "large.bin")));

        using var outerTooLarge = new NonSeekReadStream(new byte[70 * 1024]);
        var outerOverflow = await Assert.ThrowsAsync<ApiException>(
            () => api.UploadAsync("files", "outer-large.bin", false, outerTooLarge));
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, outerOverflow.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(host.Root, "outer-large.bin")));
        AssertTemporaryDirectoryEmpty(host.Root);
    }

    private static void AssertTemporaryDirectoryEmpty(string root)
    {
        var temporary = Path.Combine(root, ".dvfilestorage-tmp");
        Assert.IsFalse(Directory.Exists(temporary) && Directory.EnumerateFileSystemEntries(temporary).Any());
    }

    /// <summary>建立不连接数据库、只使用临时容器和测试认证策略的真实 HTTP 宿主。</summary>
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
        private string Address { get; }

        public static async Task<ApiHost> StartAsync(bool active, bool isAdmin, long maxUploadBytes)
        {
            var root = Path.Combine(Path.GetTempPath(), "SwiftExport-file-api-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:ApiBaseUrl"] = "http://127.0.0.1:51234/virtual/",
                ["FileStorage:Containers:files:RootPath"] = root,
                ["FileStorage:Containers:files:MaxUploadBytes"] = maxUploadBytes.ToString()
            });
            builder.Services.AddConfiguredFileStorage(builder.Configuration);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddSingleton<IPermissionEvaluationService>(
                new PermissionProjectionStub(active, isAdmin));
            builder.Services.AddScoped<IFileAccessPolicy, FileAccessPolicy>();
            builder.Services.AddScoped<IFileTransferService, FileTransferService>();
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers(options => options.Filters.Add<ServiceResultHttpFilter>())
                .AddApplicationPart(typeof(FileController).Assembly)
                .AddNewtonsoftJson();
            var app = builder.Build();
            app.UseMiddleware<GlobalExceptionMiddleware>();
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

    /// <summary>只在请求显式携带测试方案头时建立身份，避免绕过未认证场景。</summary>
    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FileApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>模拟无长度、不可 seek 的 Refit 上传流。</summary>
    private sealed class NonSeekReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
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
}
