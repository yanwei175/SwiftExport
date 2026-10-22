using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.WarehouseSites.Models;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SwiftExportClient.Navigation;
using System.Text.Json;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

/// <summary>编排供应商虚拟仓列表、分页查询、随行编辑抽屉及受权启用、停用和删除命令。</summary>
public sealed partial class WarehouseSitesViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private enum ListLoadResult
    {
        Ignored,
        Failed,
        Succeeded
    }

    /// <summary>供应商虚拟仓页面的稳定导航键。</summary>
    public const string RouteKey = "inventory-warehouse-sites";
    private const int DefaultPageSize = 20;
    private readonly IWarehouseSiteApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IWarehouseSiteReasonDialogService _reasonDialog;
    private readonly ICurrentUserService _currentUser;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly Dictionary<string, Guid> _writeIntents = new(StringComparer.Ordinal);
    private CancellationTokenSource? _listCancellation;
    private CancellationTokenSource? _detailCancellation;
    private int _listRequestVersion;
    private int _detailRequestVersion;
    private WarehouseSiteDto? _editSelectionAnchor;
    private bool _selectionLoadInProgress;
    private bool _selectionConfirmationInProgress;
    private bool _suppressSelectionCoordination;
    private bool _initialized;
    private bool _isAlive = true;
    private string? _inputError;
    private string? _summaryBeforeInputError;
    private string? _appliedQuery;

    /// <summary>创建页面编排器并接入当前会话权限快照。</summary>
    public WarehouseSitesViewModel(
        IWarehouseSiteApi api,
        IApiCallRunner runner,
        IMessageService messages,
        IDialogService dialogs,
        IWarehouseSiteReasonDialogService reasonDialog,
        ICurrentUserService currentUser,
        IReferenceDataCacheService referenceDataCache)
    {
        _api = api;
        _runner = runner;
        _messages = messages;
        _dialogs = dialogs;
        _reasonDialog = reasonDialog;
        _currentUser = currentUser;
        _referenceDataCache = referenceDataCache;
        _observedUser = CurrentUserKey;
        Title = "供应商虚拟仓";
        StatusOptions =
        [
            new WarehouseSiteStatusFilter("全部状态", null),
            new WarehouseSiteStatusFilter("活动", true),
            new WarehouseSiteStatusFilter("停用", false)
        ];
        SelectedStatus = StatusOptions[0];
        ApplyPermissions();
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        RegisterBusyAwareCommand(OpenCreateCommand);
        RegisterBusyAwareCommand(OpenEditCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(CloseDrawerCommand);
        RegisterBusyAwareCommand(ActivateCommand);
        RegisterBusyAwareCommand(DeactivateCommand);
        RegisterBusyAwareCommand(DeleteCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand);
    }

    public ObservableCollection<WarehouseSiteDto> Items { get; } = [];
    /// <summary>获取当前登录会话可见的供应商缓存，供筛选与新建抽屉本地精确选择。</summary>
    public IEnumerable<SupplierDto> SupplierOptions => _referenceDataCache.Suppliers;
    public IReadOnlyList<WarehouseSiteStatusFilter> StatusOptions { get; }
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
    public string PageSummary => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierDto? SearchSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSiteStatusFilter SelectedStatus { get; set; }
    [ObservableProperty] public partial string? ResultSummary { get; set; } = "尚未加载。";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int PageIndex { get; set; } = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int PageSize { get; set; } = DefaultPageSize;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageSummary))]
    public partial int TotalCount { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial WarehouseSiteDto? SelectedItem { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseDrawerCommand))]
    public partial bool IsDrawerOpen { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial WarehouseSiteEditModel? EditModel { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool HasManagePermission { get; private set; }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_initialized || !_isAlive) return;
        _initialized = true;
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        if (!_isAlive) return;
        OnPropertyChanged(nameof(SupplierOptions));
        if (!cacheResult.Success)
            _messages.ShowError("供应商缓存未更新", cacheResult.Message);
        await LoadWithBusyAsync(null, "正在查询虚拟仓...");
    }

    partial void OnEditModelChanging(WarehouseSiteEditModel? value)
    {
        if (EditModel is not null) EditModel.PropertyChanged -= OnEditModelPropertyChanged;
    }

    partial void OnEditModelChanged(WarehouseSiteEditModel? value)
    {
        if (value is not null) value.PropertyChanged += OnEditModelPropertyChanged;
        UpdateEditState();
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (!CanQuery()) return;
        var request = CaptureSearchRequest(1, PageSize);
        if (GetSearchInputError(request) is { } error) { ResultSummary = error; return; }
        if (!await PrepareDrawerReplacementAsync()) return;
        await LoadWithBusyAsync(null, "正在查询虚拟仓...", request);
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (_pendingWrite is not null) { await RetryWriteAsync(); return; }
        if (!CanQuery()) return;
        if (!await PrepareDrawerReplacementAsync()) return;
        await LoadWithBusyAsync(SelectedItem?.Uid, "正在刷新虚拟仓...");
    }

    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (!CanQuery() || request is null || !await PrepareDrawerReplacementAsync()) return;
        var query = AppliedSearchRequest(); query.PageIndex = request.NewIndex; query.PageSize = request.NewSize;
        await LoadWithBusyAsync(null, "正在切换分页...", query);
    }

    [RelayCommand(CanExecute = nameof(CanOpenCreate))]
    private async Task OpenCreateAsync()
    {
        if (!CanOpenCreate()) return;
        if (!await PrepareDrawerReplacementAsync()) return;
        _editSelectionAnchor = null;
        EditModel = WarehouseSiteEditModel.CreateNew();
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanOpenEdit))]
    private async Task OpenEditAsync()
    {
        if (!CanOpenEdit()) return;
        var selected = SelectedItem;
        if (selected is null || !await PrepareDrawerReplacementAsync()) return;
        var loadResult = await LoadEditTargetAsync(selected, "正在加载虚拟仓详情...");
        if (loadResult == ListLoadResult.Failed)
            _messages.ShowError("无法编辑虚拟仓", ResultSummary ?? "虚拟仓详情加载失败。");
    }

    /// <summary>协调 DataGrid 的真实选择变化；仅在编辑抽屉已打开时替换目标，并完整保护未保存内容。</summary>
    public async Task HandleSelectionChangedAsync(WarehouseSiteDto? previousItem, WarehouseSiteDto? currentItem)
    {
        var editor = EditModel;
        try
        {
            await HandleSelectionChangedCoreAsync(previousItem, currentItem);
        }
        catch (Exception)
        {
            // WPF 事件入口是 async void；在页面边界观察异常，避免未处理异常终止 UI 线程。
            if (_isAlive && ReferenceEquals(EditModel, editor))
                RestoreSelection(_editSelectionAnchor ?? previousItem);
            if (_isAlive) _messages.ShowError("无法切换虚拟仓", "切换编辑目标失败，请重试。");
        }
    }

    /// <summary>执行选行协调；同一时刻只允许一个未保存确认，旧确认不得改写已替换的编辑上下文。</summary>
    private async Task HandleSelectionChangedCoreAsync(WarehouseSiteDto? previousItem, WarehouseSiteDto? currentItem)
    {
        if (!_isAlive || _suppressSelectionCoordination || ReferenceEquals(previousItem, currentItem)) return;
        if (HasPendingWrite) { RestoreSelection(_editSelectionAnchor ?? previousItem); return; }
        if (!IsDrawerOpen || EditModel?.IsCreateMode == true) return;

        var anchor = _editSelectionAnchor ?? previousItem;
        if (_selectionConfirmationInProgress)
        {
            RestoreSelection(anchor);
            return;
        }
        if (IsBusy && !_selectionLoadInProgress)
        {
            RestoreSelection(anchor);
            return;
        }
        if (currentItem?.Uid == EditModel?.Uid)
        {
            _editSelectionAnchor = currentItem;
            return;
        }
        var editor = EditModel;
        if (editor?.HasChanges == true)
        {
            bool confirmed;
            _selectionConfirmationInProgress = true;
            try
            {
                confirmed = await _dialogs.ConfirmAsync(
                    "放弃虚拟仓修改", "当前抽屉有未保存修改，继续将放弃这些内容。", "放弃修改");
            }
            finally
            {
                _selectionConfirmationInProgress = false;
            }

            if (!_isAlive || !IsDrawerOpen || !ReferenceEquals(EditModel, editor)) return;
            if (!IsCurrentSelection(currentItem) || !confirmed)
            {
                RestoreSelection(anchor);
                return;
            }
        }
        if (!_isAlive) return;
        if (currentItem is null)
        {
            CloseDrawerCore();
            return;
        }

        // 选择已确认后清除旧编辑上下文，避免快速切换时对同一份脏数据重复询问。
        EditModel = null;
        IsDirty = false;
        var loadResult = await LoadEditTargetAsync(currentItem, "正在切换虚拟仓详情...");
        if (loadResult == ListLoadResult.Failed)
        {
            CloseDrawerCore();
            RestoreSelection(anchor);
            _messages.ShowError("无法切换虚拟仓", "虚拟仓详情加载失败。");
        }
    }

    /// <summary>执行可替代详情查询；快速选择时旧响应不得覆盖最新编辑目标。</summary>
    private async Task<ListLoadResult> LoadEditTargetAsync(WarehouseSiteDto selected, string busyMessage)
    {
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        var requestVersion = ++_detailRequestVersion;
        _selectionLoadInProgress = true;
        ServiceResult<WarehouseSiteDto>? result = null;
        await RunBusyAsync(async () =>
        {
            result = await _runner.RunAsync(
                () => _api.GetAsync(selected.Uid, cancellation.Token), cancellation.Token, "加载虚拟仓详情失败");
        }, busyMessage, false);
        if (!_isAlive || cancellation.IsCancellationRequested
            || !ReferenceEquals(_detailCancellation, cancellation) || _detailRequestVersion != requestVersion
            || SelectedItem?.Uid != selected.Uid)
        {
            if (ReferenceEquals(_detailCancellation, cancellation))
            {
                _detailCancellation = null;
                _selectionLoadInProgress = false;
            }
            cancellation.Dispose();
            return ListLoadResult.Ignored;
        }
        _detailCancellation = null;
        _selectionLoadInProgress = false;
        cancellation.Dispose();
        if (result is null || !result.Success || result.Data is null)
        {
            ResultSummary = result?.Message ?? "虚拟仓详情加载失败。";
            return ListLoadResult.Failed;
        }
        EditModel = WarehouseSiteEditModel.FromDto(result.Data);
        _editSelectionAnchor = selected;
        IsDrawerOpen = true;
        return ListLoadResult.Succeeded;
    }

    [RelayCommand(CanExecute = nameof(CanCloseDrawer))]
    private async Task CloseDrawerAsync() => await PrepareDrawerReplacementAsync();

    /// <summary>保存或恢复本页冻结的原命令，不把未知请求替换为新输入。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_pendingWrite is not null) { await RetryWriteAsync(); return; }
        if (!CanSave()) return;
        var editor = EditModel!;
        if (!editor.ValidateForSave()) { _messages.ShowWarning("资料未保存", "请检查供应商、仓库代码和仓库名称。"); return; }
        object command = editor.IsCreateMode
            ? new CreateSupplierVirtualWarehouseCommand { CommandId = editor.GetOrCreateCommandId(), SupplierId = editor.SelectedSupplier!.Uid, Code = editor.Code.Trim(), Name = editor.Name.Trim() }
            : new UpdateWarehouseSiteCommand { CommandId = editor.GetOrCreateCommandId(), ExpectedVersion = editor.Version, Name = editor.Name.Trim() };
        _pendingWrite = new FrozenWarehouseWrite(CurrentUserKey, editor.IsCreateMode ? "create" : "update", editor.Uid, editor.Version, System.Text.Json.JsonSerializer.Serialize(command), editor, null);
        NotifyWriteState();
        await RunBusyAsync(SendFrozenWriteAsync, "正在保存虚拟仓...");
    }

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private Task ActivateAsync() => CanActivate() ? StartStateWriteAsync("activate", "启用") : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private Task DeactivateAsync() => CanDeactivate() ? StartStateWriteAsync("deactivate", "停用") : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => CanDelete() ? StartStateWriteAsync("delete", "删除") : Task.CompletedTask;

    /// <summary>原因确认后复核同一账号、权限和目标，冻结完整状态命令后才发送。</summary>
    private async Task StartStateWriteAsync(string kind, string title)
    {
        var target = SelectedItem!; var version = target.Version; var user = CurrentUserKey; var session = _userSession;
        var reason = await _reasonDialog.RequestAsync(title, target);
        if (reason is null || !_isAlive || session != _userSession || user != CurrentUserKey || !HasManagePermission || IsBusy || HasPendingWrite) return;
        if (SelectedItem?.Uid != target.Uid || SelectedItem.Version != version)
        { _messages.ShowWarning("未执行" + title, "确认期间所选虚拟仓已经变化，请重新选择。"); return; }
        var key = CreateIntentKey(kind, target, reason); var id = GetOrCreateIntent(key);
        object command = kind switch
        {
            "activate" => new ActivateWarehouseSiteCommand { CommandId = id, ExpectedVersion = version, Reason = reason },
            "deactivate" => new DeactivateWarehouseSiteCommand { CommandId = id, ExpectedVersion = version, Reason = reason },
            _ => new DeleteWarehouseSiteCommand { CommandId = id, ExpectedVersion = version, Reason = reason }
        };
        _pendingWrite = new FrozenWarehouseWrite(user, kind, target.Uid, version, System.Text.Json.JsonSerializer.Serialize(command), null, key);
        NotifyWriteState(); await RunBusyAsync(SendFrozenWriteAsync, "正在" + title + "虚拟仓...");
    }
    /// <summary>在写命令已经成功后独立刷新选择器快照；缓存失败只提示，不改变已完成的业务结果。</summary>
    private async Task RefreshWarehousePickerCacheAfterWriteAsync(string failureTitle)
    {
        var user = CurrentUserKey; var session = _userSession;
        try
        {
            var cacheResult = await _referenceDataCache.RefreshWarehouseSitesAsync();
            if (_isAlive && user == CurrentUserKey && session == _userSession && !cacheResult.Success)
                _messages.ShowWarning(failureTitle, cacheResult.Message);
        }
        catch (Exception exception)
        {
            if (_isAlive && user == CurrentUserKey && session == _userSession) _messages.ShowWarning(failureTitle, exception.Message);
        }
    }

    /// <summary>加载列表并由当前用户操作持有 Busy；实际查询使用请求身份和页面存活双重校验。</summary>
    private async Task<bool> LoadWithBusyAsync(Guid? preferredId, string busyMessage, WarehouseSiteSearchRequest? request = null)
    {
        var loadResult = ListLoadResult.Ignored;
        await RunBusyAsync(async () => loadResult = await LoadCoreAsync(preferredId, request), busyMessage, false);
        if (loadResult == ListLoadResult.Failed && _isAlive && !string.IsNullOrWhiteSpace(ResultSummary))
            _messages.ShowError("虚拟仓列表未更新", ResultSummary);
        return loadResult == ListLoadResult.Succeeded;
    }

    /// <summary>执行可替代列表查询；写成功后的刷新可直接复用，避免嵌套 Busy。</summary>
    private async Task<ListLoadResult> LoadCoreAsync(Guid? preferredId, WarehouseSiteSearchRequest? request = null)
    {
        if (!_isAlive) return ListLoadResult.Ignored;
        request ??= AppliedSearchRequest();
        if (GetSearchInputError(request) is { } inputError) { ResultSummary = inputError; return ListLoadResult.Failed; }
        _listCancellation?.Cancel();
        _listCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _listCancellation = cancellation;
        var requestVersion = ++_listRequestVersion;
        var selectedId = preferredId ?? SelectedItem?.Uid;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(request, cancellation.Token), cancellation.Token, "查询虚拟仓失败");
            if (!CanAcceptListResult(cancellation, requestVersion)) return ListLoadResult.Ignored;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = string.IsNullOrWhiteSpace(result.Message) ? "虚拟仓查询失败。" : result.Message;
                return ListLoadResult.Failed;
            }
            _suppressSelectionCoordination = true;
            try
            {
                Items.Clear();
                _appliedQuery = JsonSerializer.Serialize(request);
                foreach (var item in result.Data.Items) Items.Add(item);
                TotalCount = result.Data.TotalCount;
                PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : request.PageIndex;
                PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : request.PageSize;
                SelectedItem = Items.FirstOrDefault(item => item.Uid == selectedId) ?? Items.FirstOrDefault();
            }
            finally
            {
                _suppressSelectionCoordination = false;
            }
            ResultSummary = $"共 {TotalCount} 个虚拟仓，本页 {Items.Count} 条。";
            return ListLoadResult.Succeeded;
        }
        finally
        {
            if (ReferenceEquals(_listCancellation, cancellation)) _listCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>关闭或替换抽屉前保护未保存内容；写命令期间拒绝关闭。</summary>
    private async Task<bool> PrepareDrawerReplacementAsync()
    {
        if (!_isAlive || HasPendingWrite) return false;
        if (!IsDrawerOpen) return true;
        if (IsBusy || _selectionConfirmationInProgress) return false;
        if (EditModel?.HasChanges == true && !await _dialogs.ConfirmAsync(
                "放弃虚拟仓修改", "当前抽屉有未保存修改，继续将放弃这些内容。", "放弃修改"))
            return false;
        CloseDrawerCore();
        return true;
    }

    /// <summary>只在已经通过未保存检查或明确保存成功后清理抽屉状态。</summary>
    private void CloseDrawerCore()
    {
        CancelAndDispose(ref _detailCancellation);
        _detailRequestVersion++;
        _selectionLoadInProgress = false;
        IsDrawerOpen = false;
        EditModel = null;
        _editSelectionAnchor = null;
        IsDirty = false;
    }

    /// <summary>在取消切换或写命令 Busy 时恢复编辑目标对应行，并抑制递归选择协调。</summary>
    private void RestoreSelection(WarehouseSiteDto? anchor)
    {
        _suppressSelectionCoordination = true;
        try
        {
            SelectedItem = anchor is null ? null : Items.FirstOrDefault(item => item.Uid == anchor.Uid) ?? anchor;
        }
        finally
        {
            _suppressSelectionCoordination = false;
        }
    }

    /// <summary>确认等待结束后核对用户仍停留在发起确认的目标，避免旧结果处理后续选择。</summary>
    private bool IsCurrentSelection(WarehouseSiteDto? expected) =>
        expected is null ? SelectedItem is null : SelectedItem?.Uid == expected.Uid;

    private bool CanAcceptListResult(CancellationTokenSource source, int version) =>
        _isAlive && !source.IsCancellationRequested && ReferenceEquals(_listCancellation, source) && _listRequestVersion == version;

    private Guid GetOrCreateIntent(string key)
    {
        if (_writeIntents.TryGetValue(key, out var commandId)) return commandId;
        commandId = Guid.NewGuid();
        _writeIntents[key] = commandId;
        return commandId;
    }

    private static string CreateIntentKey(string operation, WarehouseSiteDto item, string reason) =>
        $"{operation}|{item.Uid:N}|{item.Version}|{reason.Trim()}";

    private bool CanOpenCreate() => CanEditInputs && !IsDrawerOpen;
    private bool CanOpenEdit() => CanEditInputs && !IsDrawerOpen && SelectedItem is not null;
    private bool CanSave() => HasPendingWrite ? CanRetryWrite() : CanEditInputs && IsDrawerOpen && EditModel?.HasChanges == true;
    private bool CanCloseDrawer() => _isAlive && IsDrawerOpen && IsNotBusy && !HasPendingWrite;
    private bool CanActivate() => CanEditInputs && !IsDrawerOpen && SelectedItem?.IsActive == false;
    private bool CanDeactivate() => CanEditInputs && !IsDrawerOpen && SelectedItem?.IsActive == true;
    private bool CanDelete() => CanEditInputs && !IsDrawerOpen && SelectedItem is not null;
    private bool CanQuery() => _isAlive && !HasPendingWrite && (!IsBusy || _listCancellation is not null);
    private bool CanRefresh() => HasPendingWrite ? CanRetryWrite() : CanQuery();
    private bool CanSearch() => CanQuery() && GetSearchInputError(CaptureSearchRequest(1, PageSize)) is null;

    /// <summary>查询只在成功后接纳条件，后续分页和刷新不读取尚未应用的输入。</summary>
    private WarehouseSiteSearchRequest AppliedSearchRequest() => _appliedQuery is null ? CaptureSearchRequest(PageIndex, PageSize) : JsonSerializer.Deserialize<WarehouseSiteSearchRequest>(_appliedQuery)!;
    private WarehouseSiteSearchRequest CaptureSearchRequest(int page, int size) => new()
    { Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(), SupplierId = SearchSupplier?.Uid, IsActive = SelectedStatus.Value, PageIndex = page, PageSize = size };
    private static string? GetSearchInputError(WarehouseSiteSearchRequest request) => request.Keyword?.Length > WarehouseSiteLimits.SearchKeywordMaxLength
        ? $"关键词不能超过 {WarehouseSiteLimits.SearchKeywordMaxLength} 字。" : request.PageIndex < 1 || request.PageSize is < 1 or > WarehouseSiteLimits.MaxPageSize
            ? $"分页参数无效，每页条数须为 1–{WarehouseSiteLimits.MaxPageSize}。" : null;
    partial void OnKeywordChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        var error = GetSearchInputError(CaptureSearchRequest(1, PageSize));
        if (error is not null)
        {
            if (_inputError is null || ResultSummary != _inputError) _summaryBeforeInputError = ResultSummary;
            _inputError = error; ResultSummary = error;
        }
        else
        {
            // 只撤销本地校验提示，不覆盖其间已产生的命令结果。
            if (_inputError is not null && ResultSummary == _inputError) ResultSummary = _summaryBeforeInputError;
            _inputError = null; _summaryBeforeInputError = null;
        }
    }

    private void OnEditModelPropertyChanged(object? sender, PropertyChangedEventArgs args) => UpdateEditState();

    private void UpdateEditState()
    {
        IsDirty = HasUnsavedChanges;
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnCurrentUserChanged(object? sender, EventArgs args)
    {
        _userSession++; CancelAndDispose(ref _listCancellation); CancelAndDispose(ref _detailCancellation);
        if (_observedUser != CurrentUserKey)
        {
            _observedUser = CurrentUserKey; _appliedQuery = null; Items.Clear(); TotalCount = 0; SelectedItem = null; EditModel = null; IsDrawerOpen = false;
            if (_pendingWrite?.User == CurrentUserKey && _pendingWrite.Editor is { } editor) { EditModel = editor; IsDrawerOpen = true; }
            ResultSummary = HasPendingWrite ? "本页仍有待核实原命令，请由原账号恢复；请求仅在本页内存保留。" : "账号已变化，请重新查询。";
        }
        ApplyPermissions(); NotifyWriteState();
    }

    /// <summary>忙碌和只读状态变化同步字段禁用及导航关闭门禁。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    { base.OnPropertyChanged(e); if (e.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) NotifyWriteState(); }

    /// <summary>根据会话权限更新页面操作能力；后端仍会独立重验。</summary>
    private void ApplyPermissions()
    {
        HasManagePermission = _currentUser.HasPermission(PermissionKeys.InventoryWarehouseManage);
        IsReadOnly = !HasManagePermission;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_isAlive) return;
        _isAlive = false;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        if (EditModel is not null) EditModel.PropertyChanged -= OnEditModelPropertyChanged;
        CancelAndDispose(ref _listCancellation);
        CancelAndDispose(ref _detailCancellation);
    }

    /// <summary>取消并释放页面拥有的查询资源；重复释放安全。</summary>
    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var current = source;
        source = null;
        current?.Cancel();
        current?.Dispose();
    }
}
