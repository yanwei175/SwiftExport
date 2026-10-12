using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Mapster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.ProductMasterImportExport.ViewModels;

/// <summary>编排独立父级导入批次，冻结预览依据并隔离页面、账号及服务器变化。</summary>
public sealed partial class ProductMasterImportExportViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>仅从父级更多功能进入的页签路由。</summary>
    public const string RouteKey = "product-master-import-export";
    private readonly Func<IProductMasterImportExportApi> _api;
    private readonly ProductMasterWorkbookService _workbooks;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly ISessionService _session;
    private readonly IOptions<ApiOptions> _options;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly object _identityLock = new();
    private CancellationTokenSource _lifetime = new();
    private ProductMasterExportRequest _filter = new();
    private int _revision;
    private int _previewRevision = -1;
    private bool _disposed;
    private bool _completed;
    private int _identityRevision;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>复用文件读取、窄 Refit API、会话及页面公共设施，不访问来源页面或业务仓储。</summary>
    public ProductMasterImportExportViewModel(Func<IProductMasterImportExportApi> api, ProductMasterWorkbookService workbooks,
        IApiCallRunner runner, ICurrentUserService user, ISessionService session, IOptions<ApiOptions> options,
        IDialogService dialogs, ILogger<ProductMasterImportExportViewModel> logger)
    {
        _api = api; _workbooks = workbooks; _runner = runner; _user = user; _session = session; _options = options; _dialogs = dialogs; _logger = logger;
        Title = "父级型号批量导入导出";
        _user.CurrentUserChanged += IdentityChanged;
        _session.SessionEnded += SessionEnded;
        foreach (var command in new IRelayCommand[] { LoadFileCommand, ReadSheetCommand, PreviewCommand, ImportCommand, RemoveRowCommand, DownloadTemplateCommand, ExportCommand }) RegisterBusyAwareCommand(command);
    }
    /// <summary>当前批次输入及原行号。</summary>
    public ObservableCollection<ProductMasterImportLineModel> Rows { get; } = [];
    /// <summary>读取到的工作表列表。</summary>
    public ObservableCollection<string> Worksheets { get; } = [];
    /// <summary>当前选择的文件路径，用于工作表选择和后续读取。</summary>
    [ObservableProperty] public partial string FilePath { get; set; } = string.Empty;
    /// <summary>已成功读取批次的真实来源，切换选择或读取失败不会改写。</summary>
    [ObservableProperty] public partial string BatchSource { get; set; } = "尚未读取导入批次。";
    /// <summary>用户明确选择的工作表。</summary>
    [ObservableProperty] public partial string SelectedSheet { get; set; } = string.Empty;
    /// <summary>待移除或查看差异的当前行。</summary>
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(RemoveRowCommand))]
    public partial ProductMasterImportLineModel? SelectedRow { get; set; }
    /// <summary>来源页已应用的筛选说明。</summary>
    [ObservableProperty] public partial string FilterSummary { get; set; } = string.Empty;
    /// <summary>可理解的进度及结果摘要。</summary>
    [ObservableProperty] public partial string Summary { get; set; } = "请选择 XLSX 文件，或下载空白导入模板。";
    /// <summary>提供编辑事务桥接，不携带 DataGrid 或 View 引用。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>文件按钮与选择器使用同一访问及 Busy 准入。</summary>
    public bool CanUseFiles => CanRead();
    /// <summary>提交权限单独表达，查看页面不要求管理员。</summary>
    public string UploadPermissionHint => _user.IsAdmin ? "提交只更新父级资料，空白保留原值。" : "仅管理员可以提交导入；仍可下载模板、预览及导出。";
    /// <summary>写入或已完成批次禁止继续编辑；本地编辑沿用父级创建或编辑能力。</summary>
    public bool IsGridReadOnly => IsBusy || _completed || !_user.HasPermission(PermissionKeys.ProductsMasterCreate) && !_user.HasPermission(PermissionKeys.ProductsMasterEdit);
    /// <inheritdoc />
    public string DocumentTitle => Title;
    /// <inheritdoc />
    public bool HasUnsavedChanges => Rows.Count > 0 && !_completed;

    /// <inheritdoc />
    public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        _filter = parameter is ProductMasterImportOpenRequest request ? request.Filter.Adapt<ProductMasterExportRequest>() : new();
        FilterSummary = $"打开时的已应用筛选：关键词“{_filter.Keyword ?? string.Empty}”，分类 {_filter.CategoryIds.Count} 项，含未分类：{(_filter.IncludeUncategorized ? "是" : "否")}。仅导出已保存资料。";
        return Task.CompletedTask;
    }

    /// <summary>先检查未提交批次，再读取工作表目录和默认表；过期身份不能接纳结果。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task LoadFileAsync(string path) => RunBusyAsync(async () =>
    {
        if (!await CanReplaceAsync()) return;
        var stamp = Capture();
        var sheets = await _workbooks.InspectAsync(path, stamp.Token);
        var selected = sheets.Contains("父级型号") ? "父级型号" : sheets.FirstOrDefault() ?? string.Empty;
        if (!Current(stamp)) return;
        // 先发布可选工作表，首表不是导入表时仍允许选择其它表；原批次来源单独保留。
        FilePath = path;
        Worksheets.Clear(); foreach (var sheet in sheets) Worksheets.Add(sheet);
        SelectedSheet = selected;
        InvalidatePreview();
        await ReadCoreAsync(stamp);
    }, "正在读取 Excel…");

    /// <summary>工作表切换由明确命令执行，属性 setter 不读取文件。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task ReadSheetAsync() => RunBusyAsync(async () =>
    { if (await CanReplaceAsync()) await ReadCoreAsync(Capture()); }, "正在读取工作表…");

    /// <summary>先提交编辑并检查文件错误，再只读匹配；后到响应不能覆盖更新后的输入。</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task PreviewAsync()
    {
        // 原生编辑必须在 Busy 冻结表格前完成，否则最后一个单元格可能被取消。
        if (CommitPendingEdits?.Invoke() == false) return Task.CompletedTask;
        return RunBusyAsync(async () =>
        {
            if (Rows.Any(row => row.HasFileErrors)) { Summary = "请先修正文件读取错误，或移除错误行。"; return; }
            // 新校验失败或取消时，旧预览不能继续启用提交。
            InvalidatePreview();
            var stamp = Capture(); var revision = _revision;
            var input = Rows.Select(row => row.ToInput()).ToList();
            var result = await _runner.RunAsync(() => _api().PreviewAsync(new() { Rows = input }, stamp.Token), stamp.Token, "校验匹配失败");
            if (!Current(stamp) || revision != _revision) return;
            if (!result.Success || result.Data is null) { Summary = result.Message; return; }
            var byRow = result.Data.Rows.ToDictionary(row => row.SourceRowNumber);
            if (byRow.Count != Rows.Count || Rows.Any(row => !byRow.ContainsKey(row.SourceRowNumber))) { Summary = "预览回执不完整，请重新校验。"; return; }
            foreach (var row in Rows) row.ApplyPreview(byRow[row.SourceRowNumber]);
            _previewRevision = revision;
            Summary = $"共 {Rows.Count} 行：新增 {result.Data.Rows.Count(row => row.Operation == "Create")}，更新 {result.Data.Rows.Count(row => row.Operation == "Update")}，无变化 {result.Data.Rows.Count(row => row.Operation == "Unchanged")}，错误 {result.Data.Rows.Count(row => row.Issues.Count > 0)}。";
            NotifyState();
        }, "正在校验及匹配…");
    }

    /// <summary>冻结确认依据后一次提交，不在结果未知时自动重试或清空输入。</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ImportAsync()
    {
        if (CommitPendingEdits?.Invoke() == false) return Task.CompletedTask;
        return RunBusyAsync(async () =>
        {
            if (!ReadyToImport()) return;
            var stamp = Capture(); var revision = _revision;
            if (!await _dialogs.ConfirmAsync("确认导入父级型号", "将按预览新增或更新父级资料。空白保留原值，不下发子级资料；任一失败整批回滚。", "提交导入")) return;
            if (!Current(stamp) || revision != _revision || !ReadyToImport()) return;
            var request = new ProductMasterImportRequest { Rows = Rows.Select(row => new ProductMasterImportCommitRow
            {
                Input = row.ToInput(), ProductMasterId = row.Preview!.ProductMasterId, ExpectedVersion = row.Preview.ExpectedVersion,
                ExpectedCategoryId = row.Preview.ProposedData.CategoryId, ExpectedSupplierId = row.Preview.ProposedData.DefaultSupplierId
            }).ToList() };
            var result = await _runner.RunAsync(() => _api().ImportAsync(request, stamp.Token), stamp.Token, "导入结果无法确认，请保留批次并重新查询，勿自动重试");
            if (!Current(stamp)) return;
            if (!result.Success || result.Data is null) { _previewRevision = -1; Summary = result.Message + " 请重新预览；网络结果不明时先查询已保存资料。"; NotifyState(); return; }
            _completed = true;
            var changed = result.Data.Rows.Count(row => row.Operation != "Unchanged");
            Summary = $"导入完成，实际保存 {changed} 行；子级资料未下发。";
            WeakReferenceMessenger.Default.Send(new ProductMasterImportCompletedMessage(changed));
            NotifyState();
        }, "正在原子提交导入…");
    }

    /// <summary>移除本地输入行，不产生数据库删除操作。</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveRow()
    {
        if (SelectedRow is not { } row) return;
        row.PropertyChanged -= RowChanged;
        Rows.Remove(row); SelectedRow = Rows.FirstOrDefault(); InvalidatePreview();
    }
    /// <summary>请求停止本次操作并拒绝迟到回执；提交已到达服务器时需先查询结果。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelOperation()
    {
        lock (_identityLock) { _identityRevision++; _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new(); }
        InvalidatePreview();
        Summary = "已请求停止，当前输入已保留。若正在提交导入，请先查询已保存资料确认结果。";
    }
    /// <summary>按固定十一字段在本机生成模板。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task DownloadTemplateAsync(ProductMasterFileTarget target) => RunBusyAsync(async () =>
    { await _workbooks.WriteTemplateAsync(target, _lifetime.Token); Summary = "导入模板已保存。主型号为必填红色表头，第二行起填写。"; }, "正在生成模板…");
    /// <summary>在线取得已保存筛选数据，身份保护覆盖最终文件发布点。</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private Task ExportAsync(ProductMasterFileTarget target) => RunBusyAsync(async () =>
    {
        var stamp = Capture();
        var result = await _runner.RunAsync(() => _api().ExportAsync(_filter.Adapt<ProductMasterExportRequest>(), stamp.Token), stamp.Token, "导出查询失败");
        if (!Current(stamp)) return;
        if (!result.Success || result.Data is null) { Summary = result.Message; return; }
        await _workbooks.WriteAsync(target, result.Data, stamp.Token, publish =>
        { lock (_identityLock) { if (!Current(stamp)) throw new OperationCanceledException(); publish(); } });
        if (Current(stamp)) Summary = $"已导出 {result.Data.Count} 行已保存的父级资料。";
    }, "正在导出筛选数据…");

    /// <inheritdoc />
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken) => !IsBusy && (!HasUnsavedChanges || await CanReplaceAsync());

    /// <summary>未知技术错误由本层记录，页面只显示可理解反馈。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { if (exception is FileOperationException file) Summary = file.Message; else { _logger.LogError(exception, "父级工作簿操作失败"); Summary = "文件操作失败，请检查工作簿及保存路径。"; } return Task.CompletedTask; }

    private bool CanRead() => !_disposed && IsNotBusy && _user.IsAuthenticated && _user.HasPermission(PermissionKeys.ProductsMasterView);
    private bool CanCancel() => !_disposed && IsBusy;
    private bool CanPreview() => CanRead() && Rows.Count > 0 && !_completed;
    private bool CanImport() => CanRead() && ReadyToImport();
    private bool ReadyToImport() => _user.IsAuthenticated && _user.IsAdmin && !_completed && _previewRevision == _revision
        && Rows.Count > 0 && Rows.All(row => !row.HasFileErrors && row.Preview is { Operation: not "Invalid", Issues.Count: 0 })
        && Rows.Any(row => row.Preview!.Operation is "Create" or "Update");
    private bool CanRemove() => CanRead() && !_completed && SelectedRow is not null;
    private Task<bool> CanReplaceAsync() => !HasUnsavedChanges ? Task.FromResult(true) : _dialogs.ConfirmAsync("放弃当前导入批次", "当前批次尚未提交，是否放弃本地输入？", "放弃");

    /// <summary>完整读取通过后才替换原批次；失败或取消保留旧输入。</summary>
    private async Task ReadCoreAsync((object? User, string Server, int IdentityRevision, CancellationToken Token) stamp)
    {
        if (string.IsNullOrWhiteSpace(SelectedSheet)) return;
        var parsed = await _workbooks.ReadAsync(FilePath, SelectedSheet, stamp.Token);
        if (!Current(stamp)) return;
        BatchSource = $"本批已读来源：{FilePath}；工作表：{SelectedSheet}";
        ApplyRead(parsed);
    }
    /// <summary>一次发布完整读取结果，来源和输入不会在失败时出现错配。</summary>
    private void ApplyRead((IReadOnlyList<ProductMasterImportLineModel> Rows, string Warning) parsed)
    {
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        Rows.Clear();
        foreach (var row in parsed.Rows) { row.PropertyChanged += RowChanged; Rows.Add(row); }
        _completed = false; SelectedRow = Rows.FirstOrDefault(); InvalidatePreview();
        Summary = $"已读取 {Rows.Count} 行。{parsed.Warning} 请校验匹配后再提交。";
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    { if (ProductMasterWorkbookService.InputKeys.Contains(e.PropertyName ?? string.Empty)) InvalidatePreview(); }
    private void InvalidatePreview()
    { _revision++; _previewRevision = -1; foreach (var row in Rows) row.ApplyPreview(null); NotifyState(); }
    private (object? User,string Server,int IdentityRevision,CancellationToken Token) Capture()
    { lock (_identityLock) return (_user.CurrentUser,_options.Value.BaseUrl,_identityRevision,_lifetime.Token); }
    private bool Current((object? User,string Server,int IdentityRevision,CancellationToken Token) stamp) => !_disposed && stamp.IdentityRevision == _identityRevision && ReferenceEquals(stamp.User,_user.CurrentUser)
        && string.Equals(stamp.Server,_options.Value.BaseUrl,StringComparison.Ordinal) && _user.IsAuthenticated && _user.HasPermission(PermissionKeys.ProductsMasterView);
    private void IdentityChanged(object? sender, EventArgs e)
    {
        lock (_identityLock) { _identityRevision++; _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new(); }
        void Update() { if (_disposed) return; InvalidatePreview(); Summary = "身份或权限已变化，请重新校验。"; NotifyState(); }
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext) _uiContext.Post(_ => Update(),null);
        else Update();
    }
    private void SessionEnded(object? sender, SessionEndedEventArgs e) => IdentityChanged(sender,e);
    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsGridReadOnly)); OnPropertyChanged(nameof(CanUseFiles)); OnPropertyChanged(nameof(UploadPermissionHint)); OnPropertyChanged(nameof(HasUnsavedChanges));
        foreach (var command in new IRelayCommand[] { LoadFileCommand,ReadSheetCommand,PreviewCommand,ImportCommand,RemoveRowCommand,DownloadTemplateCommand,ExportCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>Busy 变化同步冻结真实表格及文件按钮，避免请求执行时继续编辑。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsGridReadOnly)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanUseFiles)));
            CancelOperationCommand.NotifyCanExecuteChanged();
        }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        lock (_identityLock) { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
        _user.CurrentUserChanged -= IdentityChanged; _session.SessionEnded -= SessionEnded;
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        CommitPendingEdits = null;
    }
}
