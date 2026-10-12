using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.ServerTemplates;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates;

/// <summary>为公共服务测试提供独立目录、受控目录 API、真实 XLSX 输入及可观察下载和生成边界。</summary>
internal sealed class ServerTemplateTestContext : IDisposable
{
    internal static readonly ExportTemplateContract Contract = new("SalesOrder", "sales-order-v1", 1);
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "SwiftExport-server-template-" + Guid.NewGuid().ToString("N"));
    internal string CachePath => Path.Combine(Root, "cache");
    internal byte[] Bytes { get; }
    internal CatalogApi Api { get; } = new();
    internal Func<IExportTemplateApi>? ApiFactory { get; set; }
    internal DownloadClientFactory Factory { get; } = new();
    internal CurrentUserService User { get; } = new();
    internal TestSession Session { get; }
    internal RecordingRenderer Renderer { get; } = new();
    internal ServerExcelTemplateService Service { get; }

    /// <summary>每次构造只使用本测试的临时根目录，不访问真实服务器或个人缓存。</summary>
    internal ServerTemplateTestContext(Func<ServerTemplateCacheOptions, ServerTemplateCacheOptions>? configure = null)
    {
        Directory.CreateDirectory(Root);
        using var input = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        Bytes = buffer.ToArray();
        Api.Items.Add(Info());
        Factory.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(Bytes) });
        User.SetCurrentUser(new MeDto { UserId = "user-a", Permissions = [] });
        Session = new TestSession(User);
        var options = new ServerTemplateCacheOptions { CacheRootPath = CachePath };
        Service = new ServerExcelTemplateService(() => ApiFactory?.Invoke() ?? Api, Factory, User, Session, Renderer,
            configure?.Invoke(options) ?? options);
    }

    /// <summary>建立与真实文件相符的目录项，允许用独立版本与 ID 验证缓存隔离。</summary>
    internal ExportTemplateInfoDto Info(string id = "sales-standard", int version = 1, byte[]? bytes = null)
    {
        bytes ??= Bytes;
        return new ExportTemplateInfoDto
        {
            TemplateId = id, DisplayName = "销售单标准模板", DocumentType = Contract.DocumentType,
            DataContractKey = Contract.DataContractKey, DataContractVersion = Contract.DataContractVersion,
            TemplateVersion = version, FileLength = bytes.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        };
    }

    internal ServerTemplateExportRequest Request(string id = "sales-standard", int version = 1) =>
        new(id, version, Contract, TemplateTestFixture.DetailData());
    internal string Output(string name = "output") => Path.Combine(Root, name + ".xlsx");

    /// <summary>仅删除本测试直接创建的文件和两层目录，不递归清理外部路径。</summary>
    public void Dispose()
    {
        Service.Dispose();
        Factory.Handler.Dispose();
        if (Directory.Exists(CachePath))
        {
            foreach (var file in Directory.GetFiles(CachePath)) File.Delete(file);
            Directory.Delete(CachePath);
        }
        foreach (var file in Directory.GetFiles(Root)) File.Delete(file);
        Directory.Delete(Root);
    }

    /// <summary>按公开契约返回受控目录，可延迟或故意忽略取消以验证旧响应保护。</summary>
    internal sealed class CatalogApi : IExportTemplateApi
    {
        private int _calls;
        internal List<ExportTemplateInfoDto> Items { get; } = [];
        internal int Calls => Volatile.Read(ref _calls);
        internal Action<int>? Called { get; set; }
        internal Func<CancellationToken, Task<ServiceResult<List<ExportTemplateInfoDto>>>>? Response { get; set; }
        /// <summary>使用可控响应模拟服务器目录，调用方仍通过真实公开客户端方法消费。</summary>
        public Task<ServiceResult<List<ExportTemplateInfoDto>>> ListAsync(string documentType, string dataContractKey,
            int dataContractVersion, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _calls);
            Called?.Invoke(count);
            return Response?.Invoke(cancellationToken) ?? Task.FromResult(ServiceResult<List<ExportTemplateInfoDto>>.Ok(Items.ToList()));
        }
    }

    /// <summary>每次返回独立 HttpClient 外壳并保留虚拟部署前缀，共享测试传输器可观察请求。</summary>
    internal sealed class DownloadClientFactory : IHttpClientFactory
    {
        internal Uri Address { get; set; } = new("https://server-a.example/prefix/api/export-templates/");
        internal DownloadHandler Handler { get; } = new();
        /// <summary>返回拥有独立生命周期的客户端，不释放共享测试传输器。</summary>
        public HttpClient CreateClient(string name)
        {
            if (name != ServerExcelTemplateService.HttpClientName) throw new InvalidOperationException(name);
            return new HttpClient(Handler, disposeHandler: false) { BaseAddress = Address };
        }
    }

    /// <summary>下载响应由测试控制，不创建网络连接；请求 URI 和取消 Token 用于验证行为边界。</summary>
    internal sealed class DownloadHandler : HttpMessageHandler
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal ConcurrentQueue<Uri> Requests { get; } = new();
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Response { get; set; } = null!;
        /// <summary>记录公开传输行为并执行测试安排的响应阶段。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Requests.Enqueue(request.RequestUri!);
            return Response(request, cancellationToken);
        }
    }

    /// <summary>记录真正进入生成的次数，允许模拟忽略取消的同步库；文件发布仍由被测公共服务拥有。</summary>
    internal sealed class RecordingRenderer : IExcelTemplateExportService
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal Func<CancellationToken, Task>? BeforeWrite { get; set; }
        /// <summary>模拟生成到调用方缓冲，允许在写入前确定地触发会话变化。</summary>
        public async Task ExportAsync(Stream template, Stream target, ExcelTemplateData data,
            IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (BeforeWrite is not null) await BeforeWrite(cancellationToken);
            await target.WriteAsync(new byte[] { 80, 75, 3, 4 }, CancellationToken.None);
            progress?.Report(new FileOperationProgress("Completed"));
        }
        /// <summary>拒绝生成器拥有最终路径，确保测试覆盖服务器服务自身的提交保护。</summary>
        public Task ExportToFileAsync(Stream template, string targetPath, ExcelTemplateData data, bool overwrite = false,
            IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("生成器不得提前发布最终用户文件。");
    }

    /// <summary>通过现有会话事件模拟显式退出，ClearSession 路径只触发账号变化。</summary>
    internal sealed class TestSession(CurrentUserService user) : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() => user.Clear();
        /// <summary>同时清除账号和发布显式结束事件。</summary>
        public void EndSession(SessionEndReason reason)
        {
            ClearSession();
            SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
        }
    }

    /// <summary>构造没有 Content-Length 的真实响应体，让大小边界由读取过程检验。</summary>
    internal sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        /// <summary>提供无长度声明的真实字节正文。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        /// <summary>不提供可提前用于拒绝的长度，让实际读取上限生效。</summary>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    /// <summary>响应释放后发出通知，保证迟到正文已被公共服务拒绝和释放再检查缓存状态。</summary>
    internal sealed class ObservedContent(byte[] bytes, Action disposed) : ByteArrayContent(bytes)
    {
        /// <summary>正常释放真实响应正文并通知测试，不持有文件或网络连接。</summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) disposed();
        }
    }

    /// <summary>同步传递阶段进度，便于在最终提交前确定地触发取消或会话切换。</summary>
    internal sealed class StageProgress(Action<FileOperationProgress> callback) : IProgress<FileOperationProgress>
    {
        /// <summary>在真实阶段回调边界执行测试行为。</summary>
        public void Report(FileOperationProgress value) => callback(value);
    }

    /// <summary>提供可推进的缓存访问时间，不使用阻塞等待制造文件时间顺序。</summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        /// <summary>返回测试明确推进的 UTC 时间。</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
