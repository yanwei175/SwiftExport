using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductNeutralImport.Models;
using SwiftExportClient.Features.Products.ProductNeutralImport.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels;

/// <summary>编排整文件校验与串行一百行队列，完整回执推进进度，中断保留未成功的数据。</summary>
public sealed partial class ProductNeutralImportViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDisposable
{
    /// <summary>从中性产品更多功能打开的独立导入页签。</summary>
    public const string RouteKey = "product-neutral-import";
    private readonly Func<IProductNeutralManagerApi> _api;
    private readonly ProductNeutralWorkbookService _workbooks;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly ISessionService _session;
    private readonly IOptions<ApiOptions> _options;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly object _identityLock = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _disposed;
    private bool _localValid;
    private bool _started;
    private MeDto? _batchUser;
    private string? _batchServer;
    private IReadOnlyList<ProductNeutralImportRowModel> _rows = [];

    /// <summary>复用认证 HTTP 客户端、公共 Excel 读取器及页面设施，不读取来源 DataGrid。</summary>
    public ProductNeutralImportViewModel(Func<IProductNeutralManagerApi> api, ProductNeutralWorkbookService workbooks,
        IApiCallRunner runner, ICurrentUserService user, ISessionService session, IOptions<ApiOptions> options,
        IDialogService dialogs, ILogger<ProductNeutralImportViewModel> logger)
    {
        _api = api; _workbooks = workbooks; _runner = runner; _user = user; _session = session; _options = options; _dialogs = dialogs; _logger = logger;
        Title = "中性产品批量导入";
        user.CurrentUserChanged += IdentityChanged; session.SessionEnded += SessionEnded;
        foreach (var command in new IRelayCommand[] { LoadFileCommand, ReadSheetCommand, ImportCommand, DownloadTemplateCommand, DownloadErrorsCommand, PreviousPageCommand, NextPageCommand, StopCommand }) RegisterBusyAwareCommand(command);
    }
    /// <summary>完整文件数据只保留在内存，列表以一百行为一页显示。</summary>
    public IReadOnlyList<ProductNeutralImportRowModel> Rows => _rows;
    /// <summary>当前页只读队列行。</summary>
    [ObservableProperty] public partial IReadOnlyList<ProductNeutralImportRowModel> VisibleRows { get; set; } = [];
    /// <summary>当前文件工作表列表。</summary>
    [ObservableProperty] public partial IReadOnlyList<string> Worksheets { get; set; } = [];
    /// <summary>用户选择的工作表，显式读取命令后才替换队列。</summary>
    [ObservableProperty] public partial string SelectedSheet { get; set; } = string.Empty;
    /// <summary>当前选择的文件。</summary>
    [ObservableProperty] public partial string FilePath { get; set; } = string.Empty;
    /// <summary>已读取队列的真实文件与工作表。</summary>
    [ObservableProperty] public partial string BatchSource { get; set; } = "尚未加载文件";
    /// <summary>本地错误、批次进度或停止原因。</summary>
    [ObservableProperty] public partial string Summary { get; set; } = "下载模板后填写，加载文件进行统一校验。空白会清空已有对应资料。";
    /// <summary>已收到确定回执的行数，不把正在处理的批次伪装成完成。</summary>
    [ObservableProperty] public partial int CompletedCount { get; set; }
    /// <summary>完整队列行数。</summary>
    public int TotalCount => _rows.Count;
    /// <summary>内存分页展示信息。</summary>
    public string PageSummary => $"第 {_page} / {Math.Max(1, (_rows.Count + 99) / 100)} 页，共 {_rows.Count} 行";
    private int _page = 1;
    /// <summary>文件操作沿用产品查看权限。</summary>
    public bool CanUseFiles => CanRead();
    /// <summary>只有存在未成功资料时才提供下载入口。</summary>
    public bool CanDownloadResults => CanDownloadErrors();
    /// <summary>管理员限制只影响提交；服务器还会重新验证活动账号。</summary>
    public string UploadPermissionHint => _user.IsAdmin ? "每批提交 100 行，空白清空已有对应资料；不调整所属主型号。" : "只有管理员可以提交导入，仍可下载模板和加载校验。";
    /// <inheritdoc />
    public string DocumentTitle => Title;
    /// <inheritdoc />
    public bool HasUnsavedChanges => _rows.Any(row => row.Status != ProductNeutralImportStatus.Success);

    /// <summary>加载并校验完整工作表，原队列替换之前确认未保存数据。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task LoadFileAsync(string path) => RunBusyAsync(async () =>
    {
        if (!await CanReplaceAsync()) return;
        using var operation = BeginOperation(); var stamp = Capture(operation.Token);
        var sheets = await _workbooks.InspectAsync(path, stamp.Token);
        if (!Current(stamp)) return;
        FilePath = path; Worksheets = sheets; SelectedSheet = sheets.Contains("中性产品") ? "中性产品" : sheets.FirstOrDefault() ?? string.Empty;
        await ReadCoreAsync(stamp);
    }, "正在加载与校验完整文件…");

    /// <summary>重新读取选中工作表，不能使用上一次工作表的校验结果。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task ReadSheetAsync() => RunBusyAsync(async () =>
    { if (!await CanReplaceAsync()) return; using var operation = BeginOperation(); await ReadCoreAsync(Capture(operation.Token)); }, "正在读取工作表…");

    /// <summary>文件校验全部通过后一次启动串行队列；失败与结果未知的行不在本次自动重试。</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ImportAsync() => RunBusyAsync(async () =>
    {
        if (!Ready()) return;
        using var operation = BeginOperation(); var stamp = Capture(operation.Token); _started = true;
        NotifyState();
        try
        {
            for (var offset = 0; offset < _rows.Count; offset += 100)
            {
                if (!Current(stamp)) { Summary = "会话、服务器已变化或队列已停止，请核对未成功行。"; break; }
                var batch = _rows.Skip(offset).Take(100).ToArray();
                foreach (var row in batch) row.Status = ProductNeutralImportStatus.Submitting;
                Summary = $"正在提交第 {offset + 1} 至 {offset + batch.Length} 行；已完成 {CompletedCount} / {TotalCount} 行。";
                // 冻结原始输入，每次最多一个请求，复用命名 HttpClient 的连接池。
                var request = batch.Select(row => row.Input.Adapt<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import.ProductNeutralImportRowDto>()).ToList();
                var result = await _runner.RunAsync(() => _api().ImportAsync(request, stamp.Token), stamp.Token, "导入批次请求失败");
                if (!Current(stamp)) { Unknown(batch, "会话、服务器已变化或操作已取消，请核对实际保存结果"); break; }
                var items = result.Data?.Items;
                if (!result.Success || items is null || result.Data!.TotalCount != batch.Length || items.Count != batch.Length
                    || items.Any(item => item is null || !item.RowIndex.HasValue)
                    || items.Select(item => item.RowIndex).Distinct().Count() != batch.Length
                    || !items.Select(item => item.RowIndex!.Value).Order().SequenceEqual(batch.Select(row => row.SourceRowNumber).Order()))
                {
                    Unknown(batch, result.Success ? "保存回执不完整，请核对实际保存结果" : result.Message ?? "请求失败，请核对实际保存结果");
                    Summary = "队列已停止，本批结果未知；未提交行已保留，不能自动重试。"; break;
                }
                lock (_identityLock)
                {
                    // 接纳回执与会话失效共用同一边界，避免检查之后又发生换账号的竞态。
                    if (!Current(stamp)) { Unknown(batch, "会话已变化，请核对实际保存结果"); break; }
                    var byRow = items.ToDictionary(item => item.RowIndex!.Value);
                    foreach (var row in batch)
                    { var item = byRow[row.SourceRowNumber]; row.Status = item.Success ? ProductNeutralImportStatus.Success : ProductNeutralImportStatus.Failed; row.Reason = item.Success ? string.Empty : item.Message ?? "保存失败"; }
                    CompletedCount += batch.Length;
                }
            }
            if (CompletedCount == TotalCount) Summary = $"保存结束：成功 {_rows.Count(row => row.Status == ProductNeutralImportStatus.Success)} 行，失败 {_rows.Count(row => row.Status == ProductNeutralImportStatus.Failed)} 行。请刷新中性产品列表查看结果。";
            else Summary = $"队列已停止：已收到 {CompletedCount} / {TotalCount} 行回执，{_rows.Count(row => row.Status == ProductNeutralImportStatus.OutcomeUnknown)} 行结果未知，{_rows.Count(row => row.Status == ProductNeutralImportStatus.Pending)} 行未提交。请核对并下载未成功数据。";
        }
        finally
        {
            Unknown(_rows.Where(row => row.Status == ProductNeutralImportStatus.Submitting), "操作已中断，请核对实际保存结果");
            NotifyState();
        }
    }, "正在按批次保存中性产品资料…");

    /// <summary>生成本地代码设计的空白八字段模板。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task DownloadTemplateAsync(ProductNeutralImportFileTarget target) => WriteAsync(target, [], false);

    /// <summary>本地校验失败导出全文件，保存结束后只导出未成功行，最后附加原因。</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadErrors))]
    private Task DownloadErrorsAsync(ProductNeutralImportFileTarget target) => WriteAsync(target,
        (!_started ? _rows : _rows.Where(row => row.Status != ProductNeutralImportStatus.Success)).Select(row => new ProductNeutralImportRowModel(row.Input.Adapt<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import.ProductNeutralImportRowDto>()) { Status = row.Status, Reason = row.Reason }).ToArray(), true);

    /// <summary>取消当前请求并停止后续发送，服务器已提交的行不能撤销。</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() { _operation?.Cancel(); Summary = "正在停止；当前批次须核对保存结果。"; }
    private bool CanStop() => IsBusy && _operation is { IsCancellationRequested: false };
    /// <summary>切换前一页，不重新读取文件。</summary>
    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private void PreviousPage() { _page--; UpdatePage(); }
    /// <summary>切换后一页，不重新读取文件。</summary>
    [RelayCommand(CanExecute = nameof(CanNext))]
    private void NextPage() { _page++; UpdatePage(); }
    private bool CanPrevious() => IsNotBusy && _page > 1;
    private bool CanNext() => IsNotBusy && _page * 100 < _rows.Count;
    private bool CanRead() => !_disposed && IsNotBusy && _user.IsAuthenticated && _user.HasPermission(PermissionKeys.ProductsNeutralView);
    private bool CanImport() => CanRead() && Ready();
    private bool Ready() => _localValid && !_started && _rows.Count > 0 && _user.IsAdmin
        && ReferenceEquals(_batchUser, _user.CurrentUser) && _batchServer == _options.Value.BaseUrl;
    private bool CanDownloadErrors() => CanRead() && HasUnsavedChanges;

    /// <summary>接纳完整且属于当前会话的文件快照，然后执行内存分页。</summary>
    private async Task ReadCoreAsync((MeDto? User, string Server, int Generation, CancellationToken Token) stamp)
    {
        _localValid = false; NotifyState();
        var rows = await _workbooks.ReadAsync(FilePath, SelectedSheet, stamp.Token);
        if (!Current(stamp)) return;
        _rows = rows; _localValid = rows.All(row => row.Status != ProductNeutralImportStatus.Failed); _started = false;
        _batchUser = stamp.User; _batchServer = stamp.Server; CompletedCount = 0; _page = 1;
        BatchSource = $"{FilePath} / {SelectedSheet}";
        Summary = rows.Count == 0 ? "文件没有数据行。" : _localValid ? $"共 {rows.Count} 行，本地校验通过。" : $"共 {rows.Count} 行，{rows.Count(row => row.Status == ProductNeutralImportStatus.Failed)} 行校验失败；请下载全文件修改后重新加载。";
        OnPropertyChanged(nameof(Rows)); OnPropertyChanged(nameof(TotalCount)); UpdatePage(); NotifyState();
    }

    /// <summary>写文件时再次检查身份，并保护最终原子发布，防止旧会话输出迟到。</summary>
    private Task WriteAsync(ProductNeutralImportFileTarget target, IReadOnlyList<ProductNeutralImportRowModel> rows, bool reason) => RunBusyAsync(async () =>
    {
        using var operation = BeginOperation(); var stamp = Capture(operation.Token);
        await _workbooks.WriteAsync(target, rows, reason, stamp.Token, publish => { lock (_identityLock) { if (!Current(stamp)) throw new OperationCanceledException(); publish(); } });
        if (Current(stamp)) Summary = "文件已保存。";
    }, "正在生成 Excel 文件…");
    private CancellationTokenSource BeginOperation() { _operation = new(); StopCommand.NotifyCanExecuteChanged(); return _operation; }
    /// <summary>身份快照与失效事件使用同一锁，令牌停止当前操作，世代阻止旧会话回写。</summary>
    private (MeDto? User, string Server, int Generation, CancellationToken Token) Capture(CancellationToken token)
    { lock (_identityLock) return (_user.CurrentUser, _options.Value.BaseUrl, _generation, token); }
    private bool Current((MeDto? User, string Server, int Generation, CancellationToken Token) stamp)
    { lock (_identityLock) return !_disposed && !stamp.Token.IsCancellationRequested && stamp.Generation == _generation && ReferenceEquals(stamp.User, _user.CurrentUser) && stamp.Server == _options.Value.BaseUrl; }
    private static void Unknown(IEnumerable<ProductNeutralImportRowModel> rows, string reason)
    { foreach (var row in rows) { row.Status = ProductNeutralImportStatus.OutcomeUnknown; row.Reason = reason; } }
    private void UpdatePage() { VisibleRows = _rows.Skip((_page - 1) * 100).Take(100).ToArray(); OnPropertyChanged(nameof(PageSummary)); PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged(); }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(CanUseFiles)); OnPropertyChanged(nameof(CanDownloadResults)); OnPropertyChanged(nameof(UploadPermissionHint));
        foreach (var command in new IRelayCommand[] { ImportCommand, DownloadErrorsCommand, DownloadTemplateCommand, LoadFileCommand, ReadSheetCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>Busy 的边界同步文件按钮状态，避免后台完成后按钮仍保持禁用。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy)) { OnPropertyChanged(nameof(CanUseFiles)); OnPropertyChanged(nameof(CanDownloadResults)); }
    }
    private Task<bool> CanReplaceAsync() => HasUnsavedChanges ? _dialogs.ConfirmAsync("替换导入文件", "当前未成功行及原因尚未下载，确认替换？", "替换") : Task.FromResult(true);
    private void IdentityChanged(object? sender, EventArgs e) => InvalidateIdentity();
    private void SessionEnded(object? sender, SessionEndedEventArgs e) => InvalidateIdentity();
    /// <summary>立即使旧请求失效；UI 通知回到创建页签的线程。</summary>
    private void InvalidateIdentity()
    {
        lock (_identityLock) { _generation++; if (_operation is not null) { try { _operation.Cancel(); } catch (ObjectDisposedException) { } } }
        void Notify() { if (!_disposed) { Summary = "会话已变化，队列停止；再次导入前请重新加载文件。"; NotifyState(); } }
        if (_uiContext is null || SynchronizationContext.Current == _uiContext) Notify(); else _uiContext.Post(_ => Notify(), null);
    }
    /// <inheritdoc />
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges && !IsBusy) return true;
        var close = await _dialogs.ConfirmAsync("关闭导入", "关闭会停止队列并丢弃未下载的输入和原因；正在提交的批次可能已保存。", "关闭");
        if (close) { try { _operation?.Cancel(); } catch (ObjectDisposedException) { } }
        return close;
    }
    /// <inheritdoc />
    protected override Task HandleExceptionAsync(Exception exception)
    { _logger.LogError(exception, "中性产品导入文件或队列操作失败"); Summary = exception.Message; return Task.CompletedTask; }
    /// <summary>关闭页签时使全部请求失效并解绑会话通知。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        lock (_identityLock) { _disposed = true; _generation++; try { _operation?.Cancel(); } catch (ObjectDisposedException) { } }
        _user.CurrentUserChanged -= IdentityChanged; _session.SessionEnded -= SessionEnded;
    }
}
