using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.QualityControl.FlowManagement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;

/// <summary>完整流量测试组详情；本地输入和脏状态归模型，整组事务与快照仍以后端为权威。</summary>
public sealed partial class FlowDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>详情页签稳定路由，身份为 flow:头Uid，新建时使用独立临时身份。</summary>
    public const string RouteKey = "quality-control-flow-detail";
    private readonly IFlowApi _api;
    private readonly IProductApi _products;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly IReferenceDataCacheService _cache;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _materialQuery;
    private bool _initialized;
    private bool _loading;
    private bool _loaded;
    private bool _disposed;
    private bool _deleted;

    /// <summary>使用已有公共页面、头行模型、缓存、API 包装和对话框，不建立第二套 MVVM 框架。</summary>
    public FlowDetailViewModel(IFlowApi api, IProductApi products, IApiCallRunner runner,
        ICurrentUserService user, IReferenceDataCacheService cache, IDialogService dialogs, INavigationService navigation)
    {
        _api = api; _products = products; _runner = runner; _user = user; _cache = cache; _dialogs = dialogs; _navigation = navigation;
        Title = "流量测试组";
        Header.PropertyChanged += OnHeaderChanged;
        LineList.ListStateChanged += OnLinesChanged;
        PropertyChanged += OnPageChanged;
        _user.CurrentUserChanged += OnUserChanged;
        foreach (var command in Commands()) RegisterBusyAwareCommand(command);
        UpdateCapabilities();
    }

    /// <summary>头信息的身份、审计和脏跟踪由公共行模型维护。</summary>
    public FlowHeaderModel Header { get; } = new();
    /// <summary>功能行集合及删除池由公共列表维护；保存只提交最终可见集合。</summary>
    public BaseListModel<FlowLineModel, FlowLineDto> LineList { get; } = new(_ => new());
    /// <summary>可见功能行。</summary>
    public ReadOnlyObservableCollection<FlowLineModel> Lines => LineList.Items;
    /// <summary>当前物料候选，不作为历史资料缓存。</summary>
    public ObservableCollection<ProductLookupDto> MaterialOptions { get; } = [];
    /// <summary>视图负责提交编辑与检查输入转换，业务命令不依赖具体控件。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    [ObservableProperty] public partial IReadOnlyList<FlowDictionaryOption> FunctionOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<FlowDictionaryOption> LimiterOptions { get; set; } = [];
    [ObservableProperty] public partial ProductLookupDto? SelectedMaterial { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(RemoveLineCommand))] public partial FlowLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "新建一组实际测试记录。";
    [ObservableProperty] public partial bool IsMaterialSearchBusy { get; set; }
    [ObservableProperty] public partial bool HasInputErrors { get; set; }
    [ObservableProperty] public partial bool NeedsReview { get; private set; }
    [ObservableProperty] public partial string? ErrorField { get; private set; }

    /// <summary>保存过的测试组固定物料身份；初次加载失败也不会误走新增接口。</summary>
    public bool IsPersisted => Header.Uid != Guid.Empty;
    /// <summary>页签展示 SKU 和业务日期，不包含纯技术身份。</summary>
    public string DocumentTitle => IsPersisted ? $"流量测试 · {Header.SkuSnapshot}" : "流量测试 · 新建";
    /// <summary>未确定结果也参与关闭检查。</summary>
    public bool HasUnsavedChanges => !_deleted && (IsDirty || HasInputErrors || NeedsReview);
    /// <summary>当前身份拥有查看权限。</summary>
    public bool HasAccess => !_disposed && _user.HasPermission(PermissionKeys.QualityControlFlowView);
    /// <summary>字段编辑权限独立于删除权限，提交期间及未决状态禁用。</summary>
    public bool CanEdit => HasAccess && _loaded && !_deleted && !NeedsReview && IsNotBusy &&
        _user.HasPermission(IsPersisted ? PermissionKeys.QualityControlFlowEdit : PermissionKeys.QualityControlFlowCreate);
    /// <summary>物料只允许在新建记录时选择。</summary>
    public bool CanSelectMaterial => CanEdit && !IsPersisted;

    partial void OnSelectedMaterialChanged(ProductLookupDto? value)
    {
        if (_loading || !CanSelectMaterial) return;
        Header.MaterialId = value?.Uid ?? Guid.Empty;
        Header.SkuSnapshot = value?.Sku ?? string.Empty;
    }

    /// <summary>初始化一次；重复激活保留现有输入，首次详情请求可被关闭取消。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        if (!HasAccess) { ResultSummary = "当前账号没有流量查看权限。"; return; }
        var request = parameter as FlowOpenRequest;
        _loading = true;
        Header.Load(new FlowDetailDto { Uid = request?.HeaderId ?? Guid.Empty, Version = 0, TestDate = DateTime.Today });
        _loading = false;
        if (IsPersisted) await LoadAsync(cancellationToken);
        else
        {
            _loaded = true; Header.MarkAsAdded();
            await RunBusyAsync(() => LoadDictionariesAsync(cancellationToken), "正在读取字典…");
            AddLineCore(); UpdateDirty(); UpdateCapabilities();
        }
    }

    /// <summary>关闭先提交焦点内输入；提交过程中拒绝关闭，放弃未决记录时明确提示查列表核实。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        CommitPendingEdits?.Invoke();
        if (!HasUnsavedChanges) return true;
        return await _dialogs.ConfirmAsync("关闭流量测试组",
            NeedsReview ? "提交结果尚未确定。关闭后请在列表中核实本次测试组，再决定后续操作。" : "有未保存修改，确定放弃并关闭吗？",
            "放弃并关闭", "继续编辑", cancellationToken);
    }

    /// <summary>显式添加功能行，默认选择初始化字典项，测量值保持空。</summary>
    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private void AddLine() => AddLineCore();
    /// <summary>删除一行只改变本地最终集合，已保存行进入公共删除池。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveLine))]
    private void RemoveLine()
    {
        if (!CanRemoveLine() || CommitPendingEdits?.Invoke() == false) return;
        LineList.Remove(SelectedLine); SelectedLine = Lines.FirstOrDefault();
    }
    /// <summary>刷新前检查未保存输入，读取失败保留旧组；未决的已保存记录可通过刷新核实。</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (!CanReload()) return;
        CommitPendingEdits?.Invoke();
        await RunBusyAsync(async () =>
        {
            if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("重新读取测试组",
                "重新读取将替换本页尚未保存的输入，确定继续吗？", "重新读取", "继续编辑", _lifetime.Token)) return;
            await ReadDetailCoreAsync(_lifetime.Token);
        }, "正在读取测试组…");
    }
    /// <summary>显式刷新字典只更新候选，既有选择的名称快照和输入保持不变。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshDictionaries))]
    private Task RefreshDictionariesAsync() => RunBusyAsync(() => LoadDictionariesAsync(_lifetime.Token, true), "正在刷新字典…");

    /// <summary>提交完整组，临时行转换为服务器约定的空身份；失败不接纳基线或清空删除池。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        if (CommitPendingEdits?.Invoke() == false || HasInputErrors) { ValidationSummary = "请修正红框中的数字或日期输入。"; return; }
        var validation = ValidateGroup();
        if (validation is not null) { ValidationSummary = validation; return; }
        await RunWriteAsync(async () =>
        {
            // Busy 内再次复核权限，避免弹窗或会话变更后仍提交。
            if (!HasAccess || !_user.HasPermission(IsPersisted ? PermissionKeys.QualityControlFlowEdit : PermissionKeys.QualityControlFlowCreate)) return;
            var data = Header.ToEditDto();
            data.Lines = Lines.Select(x => x.ToEditDto()).ToList();
            var result = IsPersisted
                ? await _runner.RunAsync(() => _api.UpdateAsync(Header.Uid,
                    new UpdateItemRequest<FlowEditDto> { Uid = Header.Uid, Version = Header.Version, Data = data }, _lifetime.Token), _lifetime.Token, "保存测试组失败")
                : await _runner.RunAsync(() => _api.CreateAsync(new CreateItemRequest<FlowEditDto> { Data = data }, _lifetime.Token), _lifetime.Token, "建立测试组失败");
            if (_disposed || !HasAccess) return;
            if (!result.Success) { AcceptFailure(result.Error?.Code, result.Error?.Field, result.Error?.Message ?? result.Message); return; }
            if (result.Data?.Detail is null || result.Data.Detail.Uid == Guid.Empty)
            { AcceptFailure(ServiceErrorCodes.OutcomeUnknown, null, "保存回执不完整，请先查询核实测试组。"); return; }
            ApplyDetail(result.Data.Detail);
            _navigation.UpdateDocumentKey(this, $"flow:{Header.Uid:N}");
            ResultSummary = "测试组已保存。重新实测请在列表中新建测试组。";
        }, "正在保存测试组…");
    }

    /// <summary>删除按头版本执行，确认前保留输入；失败保留本页状态。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (!CanDelete()) return;
        await RunBusyAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("删除测试组", "确定删除整组流量记录吗？本页未保存的修改也将放弃。",
                "删除整组", "取消", _lifetime.Token)) return;
            if (!HasAccess || !_user.HasPermission(PermissionKeys.QualityControlFlowDelete)) return;
            var result = await _runner.RunAsync(() => _api.DeleteAsync(Header.Uid,
                new DeleteItemRequest { Uid = Header.Uid, Version = Header.Version, IsSoftDelete = true }, _lifetime.Token), _lifetime.Token, "删除测试组失败");
            if (_disposed || !HasAccess) return;
            if (!result.Success) { AcceptFailure(result.Error?.Code, result.Error?.Field, result.Error?.Message ?? result.Message); return; }
            _deleted = true; NeedsReview = false; IsDirty = false; UpdateCapabilities();
            ResultSummary = "测试组已删除，请关闭页签并刷新列表。";
        }, "正在删除测试组…");
    }

    private bool CanAddLine() => CanEdit;
    private bool CanRemoveLine() => CanEdit && SelectedLine is not null;
    private bool CanSave() => CanEdit && (IsDirty || HasInputErrors);
    private bool CanReload() => HasAccess && IsPersisted && IsNotBusy && !_deleted;
    private bool CanRefreshDictionaries() => HasAccess && IsNotBusy && !_deleted;
    private bool CanDelete() => HasAccess && IsPersisted && _loaded && !_deleted && !NeedsReview && IsNotBusy && _user.HasPermission(PermissionKeys.QualityControlFlowDelete);

    /// <summary>新增行使用本地临时身份；服务器保存后返回真正行身份。</summary>
    private void AddLineCore()
    {
        if (_disposed || !HasAccess) return;
        var function = _cache.GetDictionaryValues(DictCodes.FlowTestFunction).FirstOrDefault(x => x.DataNameEn == "Default");
        var limiter = _cache.GetDictionaryValues(DictCodes.FlowLimiterSpecification).FirstOrDefault(x => x.DataNameEn == "Unlimited" || x.DataNameZh == "不限流");
        var line = LineList.AddNew(new FlowLineDto { Uid = GuidSequentialGenerator.NewGuid(), Version = 0,
            FunctionId = function?.Uid ?? Guid.Empty, FunctionNameSnapshot = Name(function),
            LimiterSpecificationId = limiter?.Uid ?? Guid.Empty, LimiterSpecificationNameSnapshot = Name(limiter),
            SortNo = Lines.Count == 0 ? 1 : Lines.Max(x => x.SortNo) + 1 });
        line.SetOptions(FunctionOptions, LimiterOptions); SelectedLine = line;
    }

    /// <summary>组级验证覆盖最终集合、功能唯一和新选择资格；历史未变选择允许停用。</summary>
    private string? ValidateGroup()
    {
        ErrorField = null;
        if (Header.ValidateRow() is { } headerError) { ErrorField = "Header"; return headerError; }
        if (Lines.Count == 0) return "每组至少保留一个功能行。";
        var functions = new HashSet<Guid>();
        foreach (var line in Lines)
        {
            var error = line.ValidateRow();
            if (error is null && !functions.Add(line.FunctionId)) { error = "同组功能不能重复。"; line.SetFieldError(nameof(line.FunctionId), error); }
            if (error is null && ((!line.IsOriginalFunction && !FunctionOptions.Any(x => x.Uid == line.FunctionId && x.IsEnabled))
                || (!line.IsOriginalLimiter && !LimiterOptions.Any(x => x.Uid == line.LimiterSpecificationId && x.IsEnabled))))
                error = "所选字典项已不可用，请刷新字典后重新选择。";
            if (error is not null) { SelectedLine = line; ErrorField = "Lines"; return $"第 {Lines.IndexOf(line) + 1} 行：{error}"; }
        }
        return null;
    }

    /// <summary>初次读取采用令牌与请求身份；页面释放或新读取后不得接纳迟到结果。</summary>
    private async Task LoadAsync(CancellationToken token)
    {
        _load?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); _load = cts;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _runner.RunAsync(() => _api.GetByIdAsync(Header.Uid, cts.Token), cts.Token, "读取测试组失败");
                if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_load, cts) || !HasAccess) return;
                if (!result.Success || result.Data is null) { ResultSummary = result.Error?.Message ?? result.Message; return; }
                ApplyDetail(result.Data);
                await LoadDictionariesAsync(cts.Token);
            }, "正在读取测试组…");
        }
        finally { if (ReferenceEquals(_load, cts)) _load = null; cts.Dispose(); }
    }

    /// <summary>显式读取在同一最外层 Busy 中执行，不重复创建 Busy owner。</summary>
    private async Task ReadDetailCoreAsync(CancellationToken token)
    {
        var result = await _runner.RunAsync(() => _api.GetByIdAsync(Header.Uid, token), token, "读取测试组失败");
        if (_disposed || token.IsCancellationRequested || !HasAccess) return;
        if (!result.Success || result.Data is null)
        {
            if (NeedsReview && result.Error?.Code == ServiceErrorCodes.NotFound) { _deleted = true; NeedsReview = false; IsDirty = false; UpdateCapabilities(); }
            ResultSummary = result.Error?.Message ?? result.Message; return;
        }
        ApplyDetail(result.Data);
        await LoadDictionariesAsync(token);
    }

    /// <summary>读取或保存成功后接纳服务端全部行身份、版本与快照。</summary>
    private void ApplyDetail(FlowDetailDto detail)
    {
        _loading = true;
        try { Header.Load(detail); LineList.LoadFromDtos(detail.Lines); SelectedLine = Lines.FirstOrDefault(); }
        finally { _loading = false; }
        _loaded = true; NeedsReview = false; HasInputErrors = false; ValidationSummary = null; ErrorField = null;
        SetDictionaryOptions(); UpdateDirty(); UpdateCapabilities();
        OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(IsPersisted));
        ResultSummary = "已读取测试组，空测量格表示未测。";
    }

    /// <summary>使用现有会话缓存刷新字典；不复制缓存，也不因候选变化重写历史快照。</summary>
    private async Task LoadDictionariesAsync(CancellationToken token, bool refresh = false)
    {
        token.ThrowIfCancellationRequested();
        var result = refresh ? await _cache.RefreshDictionariesAsync() : await _cache.EnsureLoadedAsync();
        if (_disposed || token.IsCancellationRequested || !HasAccess) return;
        SetDictionaryOptions();
        if (!result.Success) ResultSummary = result.Message;
    }

    /// <summary>启用字典项可选；历史停用或已删除项只补入既有身份，禁用候选防止重新采用。</summary>
    private void SetDictionaryOptions()
    {
        FunctionOptions = Options(DictCodes.FlowTestFunction, Lines.Select(x => (x.FunctionId, x.FunctionNameSnapshot)));
        LimiterOptions = Options(DictCodes.FlowLimiterSpecification, Lines.Select(x => (x.LimiterSpecificationId, x.LimiterSpecificationNameSnapshot)));
        foreach (var line in Lines) line.SetOptions(FunctionOptions, LimiterOptions);
    }
    /// <summary>组装启用候选和只读历史身份；历史补项不成为其他行的新选择。</summary>
    private IReadOnlyList<FlowDictionaryOption> Options(string code, IEnumerable<(Guid Id, string Name)> history)
    {
        var options = _cache.GetDictionaryValues(code).Where(x => x.IsEnabled && !x.IsDeleted)
            .Select(x => new FlowDictionaryOption { Uid = x.Uid, Name = Name(x) }).ToList();
        foreach (var (id, name) in history.Where(x => x.Id != Guid.Empty))
            if (options.All(x => x.Uid != id)) options.Add(new() { Uid = id, Name = name, IsEnabled = false });
        return options;
    }
    private static string Name(SysDictDataDto? item) => item is null ? "" : (string.IsNullOrWhiteSpace(item.DataNameZh) ? item.DataNameEn : item.DataNameZh) ?? "";

    /// <summary>物料搜索独立于写命令，未保存且可编辑时才允许发起；当前请求拥有取消源。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchMaterialsAsync(string? keyword)
    {
        if (!CanSelectMaterial) return;
        _materialQuery?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _materialQuery = cts; IsMaterialSearchBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _products.SearchLookupAsync(new ProductLookupRequest { Keyword = keyword?.Trim() ?? "", Limit = 20 }, cts.Token), cts.Token, "查询物料失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_materialQuery, cts) || !CanSelectMaterial) return;
            if (!result.Success) { ResultSummary = result.Error?.Message ?? result.Message; return; }
            MaterialOptions.Clear(); foreach (var item in result.Data ?? []) MaterialOptions.Add(item);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_materialQuery, cts)) { _materialQuery = null; if (!_disposed) IsMaterialSearchBusy = false; } cts.Dispose(); }
    }

    /// <summary>稳定错误类别决定恢复策略；未定提交或网络中断禁止自动重发新增，字段键仅用于定位。</summary>
    private void AcceptFailure(string? code, string? field, string message)
    {
        ErrorField = field;
        if (field is "Data.MaterialId" or "Data.TestDate" or "Data.MarketDescription" or "Data.ConfigurationDescription" or "Data.Remark")
            Header.SetFieldError(field[5..], message);
        ValidationSummary = message; ResultSummary = message;
        NeedsReview = code == ServiceErrorCodes.OutcomeUnknown;
        UpdateCapabilities();
    }
    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) UpdateDirty(); }
    private void OnLinesChanged(object? sender, ListStateChangedEventArgs<FlowLineModel> args) { if (!_loading) UpdateDirty(); }
    /// <summary>整组脏状态包含头字段和公共行删除池，统一通知页签关闭及保存能力。</summary>
    private void UpdateDirty() { IsDirty = Header.IsDirty || LineList.HasChanges; OnPropertyChanged(nameof(HasUnsavedChanges)); foreach (var command in Commands()) command.NotifyCanExecuteChanged(); }
    /// <summary>未能转换到模型的文本也参与关闭检查和保存反馈。</summary>
    private void OnPageChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(IsBusy) or nameof(NeedsReview) or nameof(HasInputErrors)) UpdateCapabilities(); }
    /// <summary>查看权限撤回取消只读查询，写操作的权限在提交与接纳回执时另行复核。</summary>
    private void OnUserChanged(object? sender, EventArgs args) { if (!HasAccess) { _load?.Cancel(); _materialQuery?.Cancel(); } UpdateCapabilities(); }
    /// <summary>操作能力、字段能力和基类只读状态统一刷新；删除不依赖修改权限。</summary>
    private void UpdateCapabilities()
    {
        IsReadOnly = !HasAccess || !_user.HasPermission(IsPersisted ? PermissionKeys.QualityControlFlowEdit : PermissionKeys.QualityControlFlowCreate);
        OnPropertyChanged(nameof(HasAccess)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSelectMaterial)); OnPropertyChanged(nameof(HasUnsavedChanges));
        foreach (var command in Commands()) command.NotifyCanExecuteChanged();
    }
    private IRelayCommand[] Commands() => [SaveCommand, DeleteCommand, ReloadCommand, AddLineCommand, RemoveLineCommand, RefreshDictionariesCommand];

    /// <summary>释放生命周期和所有订阅；导航不会在写命令中关闭，强制会话释放仍阻止回写。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _load?.Cancel(); _materialQuery?.Cancel();
        Header.PropertyChanged -= OnHeaderChanged; LineList.ListStateChanged -= OnLinesChanged;
        PropertyChanged -= OnPageChanged; _user.CurrentUserChanged -= OnUserChanged; CommitPendingEdits = null;
        _lifetime.Dispose();
    }
    /// <summary>未知页面错误保留用户输入，只展示通用失败。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        if (!_disposed) ValidationSummary = "操作失败，请稍后重试。";
        return Task.CompletedTask;
    }
}

