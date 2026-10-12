extern alias backendApplication;

using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using backendApplication::SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using backendApplication::SwiftExport.Application.Interfaces.Files;
using backendApplication::SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using backendApplication::SwiftExport.Application.Services.Files;
using backendApplication::SwiftExport.Application.Services.Files.ExportTemplates;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Files;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.WebApi.Files;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.Integration;

/// <summary>以真实 Kestrel、认证和模板目录服务提供固定 XLSX；所有数据来自临时文件与内存权限投影。</summary>
internal sealed class ServerTemplateApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ActiveAccount _account;
    private int _downloads;

    private ServerTemplateApiHost(WebApplication app, ActiveAccount account, string root, string address)
    {
        _app = app;
        _account = account;
        Root = root;
        Address = new Uri(address + "/virtual/");
    }

    internal string Root { get; }
    internal Uri Address { get; }
    internal string TemplateRoot => Path.Combine(Root, "templates");
    internal int Downloads => Volatile.Read(ref _downloads);
    internal string UserId => _account.UserId.ToString();

    /// <summary>启动包含端点权限过滤器的隔离宿主，不运行生产 Program 或访问业务数据库。</summary>
    internal static async Task<ServerTemplateApiHost> StartAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "SwiftExport-template-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "templates"));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:ExportTemplates:Enabled"] = "true",
            ["FileStorage:ExportTemplates:RootPath"] = Path.Combine(root, "templates")
        });
        builder.Services.AddConfiguredFileStorage(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        var account = new ActiveAccount();
        builder.Services.AddSingleton<IPermissionEvaluationService>(account);
        builder.Services.AddScoped<IFileAccessPolicy, FileAccessPolicy>();
        builder.Services.AddScoped<IExportTemplateCatalogService, ExportTemplateCatalogService>();
        builder.Services.AddScoped<EndpointPermissionFilter>();
        builder.Services.AddAuthentication("TemplateTest")
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("TemplateTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<EndpointPermissionFilter>();
                options.Filters.Add<ServiceResultHttpFilter>();
            })
            .AddApplicationPart(typeof(ExportTemplatesController).Assembly)
            .AddNewtonsoftJson();
        var app = builder.Build();
        ServerTemplateApiHost? host = null;
        app.UsePathBase("/virtual");
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value?.EndsWith("/content", StringComparison.Ordinal) == true)
                Interlocked.Increment(ref host!._downloads);
            await next(context);
        });
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        host = new ServerTemplateApiHost(app, account, root, address);
        await host.PublishAsync("头与明细模板.xlsx", "detail", 1);
        return host;
    }

    /// <summary>在临时模板根内发布完整版本和原子清单，供刷新、停用和契约变更场景使用。</summary>
    internal async Task PublishAsync(string fixture, string contractKey, int version, bool enabled = true,
        bool includePrintFooter = false)
    {
        using var source = TemplateTestFixture.Open(fixture);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (includePrintFooter) bytes = AddPrintFooter(bytes);
        var directory = Path.Combine(TemplateRoot, "test-template", $"v{version}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "模板.xlsx"), bytes);
        var manifest = new
        {
            formatVersion = 1,
            templates = new[]
            {
                new
                {
                    templateId = "test-template", displayName = "受控测试单据", documentType = "SO",
                    dataContractKey = contractKey, dataContractVersion = 1, templateVersion = version,
                    fileLength = bytes.LongLength, sha256 = Convert.ToHexString(SHA256.HashData(bytes)), enabled
                }
            }
        };
        var temporary = Path.Combine(TemplateRoot, Guid.NewGuid().ToString("N") + ".tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest));
        File.Move(temporary, Path.Combine(TemplateRoot, "模板目录.json"), overwrite: true);
    }

    /// <summary>在隔离模板副本中加入真实 Excel 打印页脚，验证分页字段经分发和 Report 保存仍保留。</summary>
    private static byte[] AddPrintFooter(byte[] bytes)
    {
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        buffer.Position = 0;
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument worksheet;
            using (var source = entry.Open()) worksheet = XDocument.Load(source);
            var ns = TemplateTestFixture.Spreadsheet;
            worksheet.Root!.Add(new XElement(ns + "headerFooter",
                new XElement(ns + "oddFooter", "&C服务器模板 第 &P 页 / 共 &N 页")));
            entry.Delete();
            using var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open();
            worksheet.Save(output);
        }
        return buffer.ToArray();
    }

    /// <summary>模拟已登录账号被服务端实时停用，验证已有客户端缓存仍不能绕过在线检查。</summary>
    internal void DeactivateAccount() => _account.Active = false;

    /// <summary>释放 HTTP 宿主并清理仅由本夹具创建的临时目录。</summary>
    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Directory.Delete(Root, recursive: true);
    }

    /// <summary>以活动账号快照代替业务数据库，普通账号无需管理权限即可读取模板。</summary>
    private sealed class ActiveAccount : IPermissionEvaluationService
    {
        internal Guid UserId { get; } = Guid.NewGuid();
        internal bool Active { get; set; } = true;

        /// <inheritdoc/>
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active && userId == UserId ? new UserPermissionProjection() : null);

        /// <inheritdoc/>
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    /// <summary>接受真实 AuthTokenHandler 注入的测试 Bearer，匿名请求保留正常 401 行为。</summary>
    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPermissionEvaluationService account) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <inheritdoc/>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer template-test-token")
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ((ActiveAccount)account).UserId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
