using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Refit;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Files.Internal;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>在线核对模板契约和精确版本，隔离账号及服务器缓存，并在有效会话内原子生成 XLSX。</summary>
public sealed class ServerExcelTemplateService : IServerExcelTemplateService, IDisposable
{
    /// <summary>带现有认证处理器、以 /api/export-templates/ 为基址的模板下载客户端名称。</summary>
    public const string HttpClientName = "SwiftExport.ServerExcelTemplates";

    private readonly Func<IExportTemplateApi> _apiFactory;
    private readonly IHttpClientFactory _httpClients;
    private readonly ICurrentUserService _user;
    private readonly ISessionService _session;
    private readonly IExcelTemplateExportService _renderer;
    private readonly ServerTemplateCacheOptions _options;
    private readonly ServerTemplateCache _cache;
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<string, SharedDownload> _inflight = new(StringComparer.Ordinal);
    private readonly List<CancellationTokenSource> _retiredSources = [];
    private CancellationTokenSource _sessionCancellation = new();
    private string? _userId;
    private string? _server;
    private long _generation;
    private bool _disposed;

    /// <summary>使用客户端认证下载与现有模板生成器，不持有可绑定页面状态或调用方流。</summary>
    public ServerExcelTemplateService(Func<IExportTemplateApi> apiFactory, IHttpClientFactory httpClients,
        ICurrentUserService user, ISessionService session, IExcelTemplateExportService renderer)
        : this(apiFactory, httpClients, user, session, renderer, new ServerTemplateCacheOptions()) { }

    /// <summary>供普通单元测试及受控宿主注入独立缓存目录、配额与时间源。</summary>
    internal ServerExcelTemplateService(Func<IExportTemplateApi> apiFactory, IHttpClientFactory httpClients,
        ICurrentUserService user, ISessionService session, IExcelTemplateExportService renderer,
        ServerTemplateCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(apiFactory);
        ArgumentNullException.ThrowIfNull(httpClients);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(options);
        _apiFactory = apiFactory; _httpClients = httpClients; _user = user; _session = session;
        _renderer = renderer; _options = options; _cache = new ServerTemplateCache(options);
        user.CurrentUserChanged += OnCurrentUserChanged;
        session.SessionEnded += OnSessionEnded;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerExcelTemplateInfo>> ListAsync(ExportTemplateContract contract,
        CancellationToken cancellationToken = default)
    {
        ValidateContract(contract);
        var context = CaptureContext(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.Token);
        return await ReadCatalogAsync(contract, context, linked.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ExportToFileAsync(ServerTemplateExportRequest request, string targetPath,
        bool overwrite = false, IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        ValidateContract(request.Contract);
        if (!ValidId(request.TemplateId) || request.TemplateVersion <= 0)
            throw new FileOperationException("InvalidTemplateSelection", "须选择有效的模板 ID 与文件版本。");
        var context = CaptureContext(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.Token);
        var token = linked.Token;
        var fullPath = await Task.Run(() => AtomicTemplateFileWriter.ValidateTarget(targetPath, overwrite), token).ConfigureAwait(false);
        progress?.Report(new FileOperationProgress("CheckingTemplateVersion"));
        var catalog = await ReadCatalogAsync(request.Contract, context, token).ConfigureAwait(false);
        var template = catalog.SingleOrDefault(item => item.TemplateId == request.TemplateId && item.TemplateVersion == request.TemplateVersion);
        if (template is null)
            throw new FileOperationException("TemplateVersionUnavailable", "所选模板版本已停用、已更新或不匹配当前数据契约，请重新选择。");
        EnsureCurrent(context, token);
        progress?.Report(new FileOperationProgress("LoadingServerTemplate"));
        var bytes = await GetContentAsync(template, context, progress, token).ConfigureAwait(false);
        EnsureCurrent(context, token);
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new MemoryStream();
        // 生成先落到内存，输出提交仍沿用现有原子写入器；忽略取消的生成器也不能自行发布用户文件。
        await _renderer.ExportAsync(input, output, request.Data,
            progress is null ? null : new GenerationProgress(progress), token).ConfigureAwait(false);
        EnsureCurrent(context, token);
        await AtomicTemplateFileWriter.WriteAsync(output, fullPath, overwrite, progress, token,
            action => Commit(context, token, action)).ConfigureAwait(false);
    }

    /// <summary>只使用在线目录；返回基本字段的只读快照，既不自动升级选定版本，也不缓存授权结果。</summary>
    private async Task<IReadOnlyList<ServerExcelTemplateInfo>> ReadCatalogAsync(ExportTemplateContract contract,
        OperationContext context, CancellationToken token)
    {
        try
        {
            EnsureCurrent(context, token);
            var api = _apiFactory();
            EnsureCurrent(context, token);
            var result = await api.ListAsync(contract.DocumentType, contract.DataContractKey,
                contract.DataContractVersion, token).ConfigureAwait(false);
            EnsureCurrent(context, token);
            if (result is null || !result.Success || result.Data is null)
                throw new FileOperationException("TemplateCatalogFailed", "无法获取服务器模板目录。");
            var matches = result.Data.Where(item => item is not null
                && item.DocumentType == contract.DocumentType && item.DataContractKey == contract.DataContractKey
                && item.DataContractVersion == contract.DataContractVersion).Select(ToSnapshot).ToArray();
            if (matches.GroupBy(item => (item.TemplateId, item.TemplateVersion)).Any(group => group.Count() > 1))
                throw new FileOperationException("InvalidTemplateCatalog", "服务器模板目录包含重复版本。");
            return Array.AsReadOnly(matches);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (FileOperationException) { throw; }
        catch (Exception exception) when (exception is ApiException or ApiRequestException or HttpRequestException or OperationCanceledException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw RequestFailure(exception, "TemplateCatalogFailed", "读取服务器模板目录失败。");
        }
    }

    /// <summary>同会话同内容键合并下载；等待方取消只退出自身等待，任务异常始终被观察。</summary>
    private async Task<byte[]> GetContentAsync(ServerExcelTemplateInfo template, OperationContext context,
        IProgress<FileOperationProgress>? progress, CancellationToken token)
    {
        var key = ServerTemplateCache.Key(context.Server, context.UserId, template);
        var inflightKey = $"{context.Generation}:{key}";
        SharedDownload operation;
        Task<byte[]> task;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            operation = _inflight.GetOrAdd(inflightKey, _ => new SharedDownload(
                sharedToken => Task.Run(() => LoadContentAsync(key, template, context, sharedToken))));
            if (operation.TryAcquire(out task)) break;
            _inflight.TryRemove(new KeyValuePair<string, SharedDownload>(inflightKey, operation));
        }
        try
        {
            progress?.Report(new FileOperationProgress("WaitingForServerTemplate"));
            return await task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            if (operation.Release())
                _inflight.TryRemove(new KeyValuePair<string, SharedDownload>(inflightKey, operation));
        }
    }

    /// <summary>读取有效缓存或在线下载完整副本；长度、散列及无宏容器全部通过后才可发布缓存。</summary>
    private async Task<byte[]> LoadContentAsync(string key, ServerExcelTemplateInfo template, OperationContext context,
        CancellationToken sharedToken)
    {
        using var timeout = new CancellationTokenSource(_options.DownloadTimeout, _options.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, context.Token, sharedToken);
        var token = linked.Token;
        try
        {
            EnsureCurrent(context, token);
            var cached = await _cache.ReadAsync(key, template, token).ConfigureAwait(false);
            EnsureCurrent(context, token);
            if (cached is not null) return cached;
            using var client = _httpClients.CreateClient(HttpClientName);
            if (NormalizeBase(client.BaseAddress) != context.Server) throw new OperationCanceledException(context.Token);
            client.BaseAddress = new Uri(context.Server, UriKind.Absolute);
            var relativePath = $"{Uri.EscapeDataString(template.TemplateId)}/versions/{template.TemplateVersion}/content";
            using var response = await client.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw StatusFailure(response.StatusCode);
            EnsureCurrent(context, token);
            if (response.Content.Headers.ContentLength is long declaredLength && declaredLength != template.FileLength)
                throw new FileOperationException("TemplateLengthMismatch", "服务器模板响应长度与目录版本不一致。");
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var input = await FileInput.ReadBoundedAsync(source, _options.ReadLimits, token).ConfigureAwait(false);
            var bytes = input.ToArray();
            ServerTemplateCache.ValidateContent(input, bytes, template, _options.ReadLimits, token);
            EnsureCurrent(context, token);
            await _cache.StoreAsync(key, bytes, action => Commit(context, token, action), token).ConfigureAwait(false);
            EnsureCurrent(context, token);
            return bytes;
        }
        catch (OperationCanceledException) when (context.Token.IsCancellationRequested || sharedToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new FileOperationException("TemplateDownloadTimeout", "服务器模板下载超时。", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException or ApiRequestException)
        {
            if (context.Token.IsCancellationRequested || sharedToken.IsCancellationRequested) throw new OperationCanceledException(token);
            throw RequestFailure(exception, "TemplateDownloadFailed", "下载服务器模板失败。");
        }
    }

    /// <summary>捕获当前账号及服务器环境；基址变化立即使之前的下载与结果失效。</summary>
    private OperationContext CaptureContext(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var server = CurrentServer();
        CancellationTokenSource? retired = null;
        OperationContext context;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = _user.CurrentUser?.UserId;
            if (!_user.IsAuthenticated || string.IsNullOrWhiteSpace(id))
                throw new FileOperationException("TemplateAuthenticationRequired", "请先登录后选择服务器模板。");
            if (_userId != id || _server != server)
            {
                retired = RotateSession();
                _userId = id; _server = server;
            }
            context = new OperationContext(id, server, _generation, _sessionCancellation.Token);
        }
        retired?.Cancel();
        return context;
    }

    /// <summary>接纳响应、缓存与生成结果前校验身份；取消只是辅助，不信任远端或生成器遵守 Token。</summary>
    private void EnsureCurrent(OperationContext context, CancellationToken token)
    {
        var server = CurrentServer();
        DetectServerChange(server);
        lock (_stateGate) CheckCurrent(context, server, token);
    }

    /// <summary>在会话状态锁内执行单次文件移动作为提交点；发布后到达的取消不推翻已完成输出。</summary>
    private void Commit(OperationContext context, CancellationToken token, Action action)
    {
        var server = CurrentServer();
        DetectServerChange(server);
        lock (_stateGate)
        {
            CheckCurrent(context, server, token);
            action();
        }
    }

    /// <summary>调用方须持有状态锁；同时核对代数、身份、基址及取消，旧结果不得发布到新会话。</summary>
    private void CheckCurrent(OperationContext context, string server, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || context.Generation != _generation || context.UserId != _user.CurrentUser?.UserId
            || !_user.IsAuthenticated || context.Server != server || context.Server != _server)
            throw new OperationCanceledException("模板操作所属会话或服务器已变化。", token);
    }

    /// <summary>只读取命名客户端下载环境，不读取 Token 或将配置地址写入日志。</summary>
    private string CurrentServer()
    {
        using var client = _httpClients.CreateClient(HttpClientName);
        return NormalizeBase(client.BaseAddress);
    }

    /// <summary>保留虚拟目录并规范化绝对 HTTP 基址，避免不同服务器或部署前缀共用模板内容。</summary>
    private static string NormalizeBase(Uri? address)
    {
        if (address is null || !address.IsAbsoluteUri || (address.Scheme != "http" && address.Scheme != "https")
            || address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0)
            throw new FileOperationException("InvalidTemplateServer", "服务器模板客户端下载地址未正确配置。");
        return address.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/') + "/";
    }

    /// <summary>在锁内轮换会话代数及共享任务命名空间；取消在锁外进行，避免回调进入状态锁。</summary>
    private CancellationTokenSource RotateSession()
    {
        var retired = _sessionCancellation;
        _retiredSources.Add(retired);
        _sessionCancellation = new CancellationTokenSource();
        ++_generation;
        _inflight.Clear();
        return retired;
    }

    /// <summary>发现命名客户端下载环境变化时取消共享旧工作，不等待新的页面请求才能失效。</summary>
    private void DetectServerChange(string server)
    {
        CancellationTokenSource? retired = null;
        lock (_stateGate)
        {
            if (!_disposed && _server is not null && _server != server)
            {
                retired = RotateSession();
                _server = server;
            }
        }
        retired?.Cancel();
    }

    /// <summary>账号清除、权限刷新和显式退出都使旧请求失效，无需更改现有认证服务。</summary>
    private void InvalidateSession()
    {
        CancellationTokenSource? retired;
        lock (_stateGate)
        {
            if (_disposed) return;
            retired = RotateSession();
            _userId = null; _server = null;
        }
        retired.Cancel();
    }

    private void OnCurrentUserChanged(object? sender, EventArgs args) => InvalidateSession();
    private void OnSessionEnded(object? sender, SessionEndedEventArgs args) => InvalidateSession();

    /// <summary>检查数据结构标识，不通过反射推断模板兼容性，也不自动裁剪改变稳定契约键。</summary>
    private static void ValidateContract(ExportTemplateContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!ValidText(contract.DocumentType, 64) || !ValidText(contract.DataContractKey, 128) || contract.DataContractVersion <= 0)
            throw new FileOperationException("InvalidTemplateContract", "单据类型、数据契约键与版本必须明确提供。");
    }

    /// <summary>将传输 DTO 转成私有状态无关的快照；非法目录元数据不能成为下载或缓存键。</summary>
    private ServerExcelTemplateInfo ToSnapshot(ExportTemplateInfoDto item)
    {
        if (!ValidId(item.TemplateId) || !ValidText(item.DisplayName, 200) || item.TemplateVersion <= 0
            || item.FileLength <= 0 || item.FileLength > _options.ReadLimits.MaxInputBytes
            || item.Sha256 is null || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
            throw new FileOperationException("InvalidTemplateCatalog", "服务器模板目录的版本或内容描述无效。");
        return new ServerExcelTemplateInfo(item.TemplateId, item.DisplayName, item.DocumentType, item.DataContractKey,
            item.DataContractVersion, item.TemplateVersion, item.FileLength, item.Sha256.ToLowerInvariant());
    }

    private static bool ValidText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && value == value.Trim();
    private static bool ValidId(string? value) => value is not null && value.Length <= 64
        && Regex.IsMatch(value, "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>根据稳定 HTTP 状态报告授权、版本冲突或下载失败，不回传服务器响应正文。</summary>
    private static FileOperationException StatusFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new("TemplateUnauthorized", "服务器模板请求未通过认证。"),
        HttpStatusCode.Forbidden => new("TemplateForbidden", "没有使用该服务器模板的权限。"),
        HttpStatusCode.NotFound => new("TemplateVersionUnavailable", "所选服务器模板版本不可用。"),
        HttpStatusCode.Conflict => new("TemplateVersionConflict", "服务器模板版本发生冲突，请重新选择。"),
        _ => new("TemplateDownloadFailed", "下载服务器模板失败。"),
    };

    /// <summary>将 Refit 或 HTTP 传输异常归一化，保留内部异常供技术边界诊断。</summary>
    private static FileOperationException RequestFailure(Exception exception, string code, string message)
    {
        if (exception is ApiException apiException)
        {
            if (apiException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                var mapped = StatusFailure(apiException.StatusCode);
                return new FileOperationException(mapped.Code, mapped.Message, exception);
            }
        }
        if (exception is OperationCanceledException)
            return new FileOperationException("TemplateRequestTimeout", "服务器模板请求超时。", exception);
        return new FileOperationException(code, message, exception);
    }

    /// <summary>解除会话事件并取消共享工作；释放后旧结果仍受代数检查保护。</summary>
    public void Dispose()
    {
        CancellationTokenSource[] sources;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            _inflight.Clear();
            sources = [.. _retiredSources, _sessionCancellation];
        }
        _user.CurrentUserChanged -= OnCurrentUserChanged;
        _session.SessionEnded -= OnSessionEnded;
        foreach (var source in sources) source.Cancel();
        // 取消源可能仍被忽略取消的远端调用持有，不在这里提前 Dispose 使其注册失败。
    }

    /// <summary>绑定一次操作捕获的身份与环境，不持有调用方页面状态。</summary>
    private sealed record OperationContext(string UserId, string Server, long Generation, CancellationToken Token);

    /// <summary>计数独立等待者；最后一人退出时取消共享下载，其他人仍等待时取消只影响退出者。</summary>
    private sealed class SharedDownload(Func<CancellationToken, Task<byte[]>> start)
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private Task<byte[]>? _task;
        private int _waiters;
        private bool _closed;

        /// <summary>在同一锁内登记等待者并启动一次下载，关闭中的操作不能被新调用方重新采用。</summary>
        internal bool TryAcquire(out Task<byte[]> task)
        {
            lock (_gate)
            {
                if (_closed) { task = null!; return false; }
                ++_waiters;
                if (_task is null)
                {
                    _task = start(_cancellation.Token);
                    _ = _task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                task = _task;
                return true;
            }
        }

        /// <summary>最后等待者离开即关闭条目，取消不在内部锁中执行；下载结束后释放自己的取消源。</summary>
        internal bool Release()
        {
            Task<byte[]> task;
            lock (_gate)
            {
                if (--_waiters != 0) return false;
                _closed = true;
                task = _task!;
            }
            if (!task.IsCompleted) _cancellation.Cancel();
            _ = task.ContinueWith(_ => _cancellation.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }

    /// <summary>内存生成器的完成仅代表中间阶段；公开完成进度必须由最终文件发布器报告。</summary>
    private sealed class GenerationProgress(IProgress<FileOperationProgress> target) : IProgress<FileOperationProgress>
    {
        /// <summary>保留生成阶段进度，将内部缓冲完成留给最终文件发布阶段统一表达。</summary>
        public void Report(FileOperationProgress value)
        {
            if (value.Stage != "Completed") target.Report(value);
        }
    }
}
