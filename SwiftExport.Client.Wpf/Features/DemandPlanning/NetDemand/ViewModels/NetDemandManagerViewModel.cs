using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>
/// 负责物料需求工作台的分页查询、来源导航、来源建立和安全回退。
/// </summary>
public sealed partial class NetDemandManagerViewModel : ViewModelBaseEx,
    IAsyncInitializable,
    IDocumentTabViewModel,
    IDocumentTabInitializable,
    IDisposable
{
    public const string RouteKey = "net-demands";

    private readonly INetDemandManagerApi _api;
    private readonly IProcurementMasterDataQueryApi _masterDataApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly INetDemandDialogService _netDemandDialogService;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _sourceGroupCancellation;
    private CancellationTokenSource? _sourceGroupFilterDebounceCancellation;
    private CancellationTokenSource? _supplierSearchCancellation;
    private bool _initialized;
    private bool _syncingChecks;
    private bool _syncingSourceFilters;
    private bool _disposed;
    private Guid? _appliedSourceUid;
    private NetDemandManagerOpenRequest? _openRequest;
    private UpdateSuggestedPurchaseSupplierCommand? _pendingSuggestion;
    private bool _suggestionUnknown;
    private bool _suggestionInFlight;
    private readonly ICurrentUserService _currentUser;
    private readonly INavigationService? _navigation;

    public NetDemandManagerViewModel(
        INetDemandManagerApi api,
        IProcurementMasterDataQueryApi masterDataApi,
        IApiCallRunner apiCallRunner,
        IDialogService dialogService,
        IMessageService messageService,
        INetDemandDialogService netDemandDialogService,
        ICurrentUserService currentUser,
        INavigationService? navigation = null)
    {
        _api = api;
        _masterDataApi = masterDataApi;
        _apiCallRunner = apiCallRunner;
        _dialogService = dialogService;
        _messageService = messageService;
        _netDemandDialogService = netDemandDialogService;
        _currentUser = currentUser;
        _navigation = navigation;
        Title = "物料需求";

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(EstablishFromSourcesCommand);
        RegisterBusyAwareCommand(RollbackSourceEstablishmentCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(SelectSourceGroupCommand);
        RegisterBusyAwareCommand(ToggleSourcePanelCommand);
        RegisterBusyAwareCommand(ClearSourceFiltersCommand);
        RegisterBusyAwareCommand(SaveSuggestedSuppliersCommand);
        RegisterBusyAwareCommand(CancelSuggestedSupplierChangesCommand);

        SourceSourceTypes.CollectionChanged += OnSourceGroupFilterChanged;
    }

    public ObservableCollection<MaterialDemandManagerRowModel> Items { get; } = [];
    /// <summary>推荐采购供应商远程 Lookup 当前候选。</summary>
    public ObservableCollection<ProcurementSupplierCandidateDto> SuggestedSupplierCandidates { get; } = [];
    /// <summary>
    /// 左侧来源导航当前显示的来源单集合。
    /// </summary>
    public ObservableCollection<NetDemandSourceGroupModel> SourceGroups { get; } = [];

    public IReadOnlyList<NetDemandFilterOption<string>> SourceTypeOptions { get; } =
    [
        new("SO", "销售订单"),
        new("MD", "手工需求"),
        new("CR", "投诉补货"),
    ];

    public IReadOnlyList<NetDemandFilterOption<bool?>> DemandRoleOptions { get; } =
    [
        new(null, "全部角色"),
        new(false, "父件"),
        new(true, "客供件"),
    ];

    /// <summary>物料生命周期筛选，默认仅活动行。</summary>
    public IReadOnlyList<NetDemandFilterOption<NetDemandLifecycleStatusEnum?>> LifecycleOptions { get; } =
    [
        new(null, "活动"),
        new(NetDemandLifecycleStatusEnum.RolledBack, "已回退"),
        new(NetDemandLifecycleStatusEnum.Voided, "已作废"),
    ];

    /// <summary>服务端单选快捷视图。</summary>
    public IReadOnlyList<NetDemandFilterOption<string>> QuickViewOptions { get; } =
    [
        new("All", "全部"), new("Unassigned", "未承接"), new("Partial", "部分承接"),
        new("Assigned", "已承接"), new("Overdue", "逾期未承接"),
        new("DueSoon", "临期未承接"), new("Review", "需核实"),
    ];

    public ObservableCollection<NetDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    /// <summary>
    /// 来源导航独立使用的来源类型筛选。
    /// </summary>
    public ObservableCollection<NetDemandFilterOption<string>> SourceSourceTypes { get; } = [];
    public string DocumentTitle => "物料需求";
    public bool HasUnsavedChanges => _suggestionUnknown || Items.Any(item => item.IsSuggestionDirty);
    /// <summary>原行结果未知时仅允许沿原请求重试，不允许编辑或丢弃当前页。</summary>
    public bool HasPendingSuggestion => _suggestionUnknown;
    /// <summary>建议编辑权限独立于模块访问；未决和发送中冻结实际表格编辑。</summary>
    public bool CanEditSuggestion => CanWrite && _currentUser.HasPermission(PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage)
        && !_suggestionUnknown && !_suggestionInFlight
        && SelectedLifecycle?.Value is null;
    /// <summary>仅推荐建议列需要的表格只读状态。</summary>
    public bool IsSuggestionReadOnly => !CanEditSuggestion;
    public int TotalPages => IsSourceSelected || TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => IsSourceSelected ? $"当前来源共 {TotalCount} 条" : $"第 {PageIndex} / {TotalPages} 页";
    public int CheckedCount => Items.Count(item => item.IsChecked);
    /// <summary>
    /// 指示主表当前是否处于单一来源的全量加载模式。
    /// </summary>
    public bool IsSourceSelected => _appliedSourceUid.HasValue;

    /// <summary>
    /// 控制左侧来源导航是否显示。
    /// </summary>
    [ObservableProperty]
    public partial bool IsSourcePanelOpen { get; private set; }

    /// <summary>
    /// 指示来源分组请求是否正在执行。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectSourceGroupCommand))]
    public partial bool IsSourceGroupLoading { get; private set; }

    /// <summary>
    /// 显示来源单和匹配净需求数量的加载摘要。
    /// </summary>
    [ObservableProperty]
    public partial string SourceGroupSummary { get; private set; } = "打开来源导航后加载。";

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceNosText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SkusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerSkusText { get; set; } = string.Empty;

    /// <summary>推荐供应商编码或名称条件。</summary>
    [ObservableProperty]
    public partial string SuggestedSupplierKeyword { get; set; } = string.Empty;

    /// <summary>实际采购供应商编码或名称条件。</summary>
    [ObservableProperty]
    public partial string ActualSupplierKeyword { get; set; } = string.Empty;

    /// <summary>采购单号精确条件。</summary>
    [ObservableProperty]
    public partial string PurchaseOrderNoFilter { get; set; } = string.Empty;

    /// <summary>当前生命周期条件。</summary>
    [ObservableProperty]
    public partial NetDemandFilterOption<NetDemandLifecycleStatusEnum?>? SelectedLifecycle { get; set; }

    partial void OnSelectedLifecycleChanged(NetDemandFilterOption<NetDemandLifecycleStatusEnum?>? value)
    {
        OnPropertyChanged(nameof(CanEditSuggestion));
        OnPropertyChanged(nameof(IsSuggestionReadOnly));
    }

    /// <summary>当前单选快捷视图。</summary>
    [ObservableProperty]
    public partial NetDemandFilterOption<string>? SelectedQuickView { get; set; }

    /// <summary>
    /// 控制更多条件 Popup 的打开状态。
    /// </summary>
    [ObservableProperty]
    public partial bool IsMultiLineFilterOpen { get; set; }

    [ObservableProperty]
    public partial DateTime? DemandRequiredDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? DemandRequiredDateTo { get; set; }

    [ObservableProperty]
    public partial NetDemandFilterOption<bool?>? SelectedDemandRole { get; set; }

    /// <summary>
    /// 当前由左侧 ListBox 选中的来源单。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceSelected))]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    [NotifyCanExecuteChangedFor(nameof(PageChangeCommand))]
    public partial NetDemandSourceGroupModel? SelectedSourceGroup { get; set; }

    [ObservableProperty]
    public partial MaterialDemandManagerRowModel? SelectedItem { get; set; }

    /// <summary>以选中物料行的稳定身份打开只读追踪。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTrace))]
    private Task OpenTraceAsync()
    {
        var uid = SelectedItem!.Uid;
        return _navigation!.OpenDocumentAsync(MaterialDemandTraceViewModel.RouteKey,
            $"material-demand-trace:{uid:N}", new MaterialDemandTraceOpenRequest { NetDemandId = uid });
    }

    private bool CanOpenTrace() => SelectedItem is not null && _navigation is not null;

    partial void OnSelectedItemChanged(MaterialDemandManagerRowModel? value) => OpenTraceCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    public partial bool? IsAllChecked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = 20;

    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载净需求。";

    /// <summary>推荐供应商远程搜索是否正在执行。</summary>
    [ObservableProperty]
    public partial bool IsSuggestedSupplierSearchBusy { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        InitializeDefaults();
        await LoadPageAsync();
    }

    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        _initialized = true;
        InitializeDefaults();
        _openRequest = parameter as NetDemandManagerOpenRequest;
        if (!string.IsNullOrWhiteSpace(_openRequest?.SourceNo))
        {
            SourceNosText = _openRequest.SourceNo;
        }

        await LoadPageAsync(cancellationToken);
    }

    /// <summary>写命令执行中拒绝关闭；存在建议编辑时由用户明确决定是否放弃。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _suggestionUnknown)
        {
            return false;
        }

        return !HasUnsavedChanges || await _dialogService.ConfirmAsync(
            "放弃推荐供应商修改",
            "当前有尚未保存的推荐采购供应商修改，关闭页面将放弃这些修改。",
            "放弃修改");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (!await EnsureCanDiscardSuggestionChangesAsync("查询将重新加载当前页面。"))
        {
            return;
        }
        if (query is not null)
        {
            Keyword = query;
        }

        _openRequest = null;
        ExitSourceMode();
        PageIndex = 1;
        if (IsSourcePanelOpen) await RefreshSourceNavigationAsync(reloadCurrentData: false);
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardSuggestionChangesAsync("刷新将放弃未保存的推荐供应商修改。"))
        {
            return;
        }
        await RefreshManagerAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        if (!await EnsureCanDiscardSuggestionChangesAsync("清空筛选将重新加载页面。"))
        {
            return;
        }
        _openRequest = null;
        ExitSourceMode();
        Keyword = string.Empty;
        SourceNosText = string.Empty;
        SkusText = string.Empty;
        CustomerSkusText = string.Empty;
        SuggestedSupplierKeyword = string.Empty;
        ActualSupplierKeyword = string.Empty;
        PurchaseOrderNoFilter = string.Empty;
        SelectedLifecycle = LifecycleOptions[0];
        SelectedQuickView = QuickViewOptions[0];
        DemandRequiredDateFrom = null;
        DemandRequiredDateTo = null;
        SelectedDemandRole = DemandRoleOptions[0];
        SelectedSourceTypes.Clear();
        PageIndex = 1;
        if (IsSourcePanelOpen) await RefreshSourceNavigationAsync(reloadCurrentData: false);
        await LoadPageAsync();
    }

    /// <summary>打开成品需求候选；建立由对话框完成，关闭后刷新物料工作台。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private async Task EstablishFromSourcesAsync()
    {
        var hasEstablished = await _netDemandDialogService.ShowSourceDemandCandidatesAsync();
        if (hasEstablished)
        {
            await RefreshManagerAsync();
        }
    }

    /// <summary>按当前勾选行归并来源与修订后执行安全回退，活动执行关系由服务端阻断。</summary>
    [RelayCommand(CanExecute = nameof(CanRollbackSourceEstablishment))]
    private async Task RollbackSourceEstablishmentAsync()
    {
        var items = Items.Where(item => item.IsChecked)
            .GroupBy(item => item.GrossDemandId)
            .Select(group => new RollbackMaterialDemandItem
            {
                GrossDemandId = group.Key,
                DecompositionRevision = group.Select(item => item.DecompositionRevision).Distinct().Single()
            }).ToList();
        if (!await _dialogService.ConfirmAsync(
                "回退来源物料需求", $"将回退 {items.Count} 条成品需求的当前物料修订，是否继续？", "安全回退")) return;
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.RollbackSourceEstablishmentAsync(new RollbackMaterialDemandsCommand
                {
                    CommandId = Guid.NewGuid(),
                    Reason = "用户从物料需求工作台安全回退",
                    Items = items
                }),
                "回退来源物料需求失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("回退失败", result.Message);
                return;
            }
            _messageService.ShowSuccess("回退完成",
                $"已回退 {result.Data.RolledBackSourceCount} 条，已处于回退状态 {result.Data.AlreadyRolledBackCount} 条。");
            await RefreshManagerAsync();
        }, "正在回退物料需求...");
    }

    /// <summary>以 latest-request-wins 搜索有效供应商候选，旧请求不得覆盖新候选。</summary>
    [RelayCommand]
    private async Task SearchSuggestedSuppliersAsync(string? keyword)
    {
        var previous = _supplierSearchCancellation;
        var cancellation = new CancellationTokenSource();
        _supplierSearchCancellation = cancellation;
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 旧请求可能刚在其 finally 中完成释放；取消只用于加速，不影响请求身份门禁。
        }

        IsSuggestedSupplierSearchBusy = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _masterDataApi.SearchSuppliersAsync(new PagedRequestBase
                {
                    Keyword = keyword?.Trim() ?? string.Empty,
                    PageIndex = 1,
                    PageSize = 30
                }, cancellation.Token),
                cancellation.Token,
                "搜索供应商失败");
            if (cancellation.IsCancellationRequested
                || _disposed
                || !ReferenceEquals(_supplierSearchCancellation, cancellation))
            {
                return;
            }

            SuggestedSupplierCandidates.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var supplier in result.Data.Items)
                {
                    SuggestedSupplierCandidates.Add(supplier);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_supplierSearchCancellation, cancellation))
            {
                _supplierSearchCancellation = null;
                IsSuggestedSupplierSearchBusy = false;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>逐行保存全部本地建议修改；失败行及尚未提交行保持脏状态。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveSuggestedSuppliers))]
    private async Task SaveSuggestedSuppliersAsync()
    {
        // 未决行即使因编辑器迟到通知暂时不再显示为脏，也必须排在本次重试的第一位。
        var dirtyRows = _suggestionUnknown && _pendingSuggestion is not null
            ? Items.Where(item => item.Uid == _pendingSuggestion.NetDemandId)
                .Concat(Items.Where(item => item.IsSuggestionDirty && item.Uid != _pendingSuggestion.NetDemandId)).ToArray()
            : Items.Where(item => item.IsSuggestionDirty).ToArray();
        if (_suggestionUnknown && (dirtyRows.Length == 0 || dirtyRows[0].Uid != _pendingSuggestion!.NetDemandId))
        {
            _messageService.ShowError("推荐采购供应商未全部保存", "未决物料行已离开当前页面，请保持页面并核查执行结果。");
            return;
        }
        var allSaved = false;
        await RunBusyAsync(async () =>
        {
            _suggestionInFlight = true;
            NotifySuggestionState();
            try
            {
                foreach (var row in dirtyRows)
                {
                    var request = _suggestionUnknown
                        ? _pendingSuggestion!
                        : new UpdateSuggestedPurchaseSupplierCommand
                        {
                            CommandId = Guid.NewGuid(),
                            NetDemandId = row.Uid,
                            Version = row.Version,
                            SuggestedPurchaseSupplierId = row.SuggestedPurchaseSupplierId
                        };
                    // 未知行只能与原行身份相同；成功行已从脏集合移除，后续行尚未发送。
                    if (_suggestionUnknown && row.Uid != request.NetDemandId) continue;
                    var result = await _apiCallRunner.RunAsync(
                        () => _api.UpdateSuggestedPurchaseSupplierAsync(request),
                        "保存推荐采购供应商失败");
                    if (!result.Success || result.Data is null)
                    {
                        var code = result.Error?.Code;
                        var retain = code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable
                            || (_suggestionUnknown && code is ServiceErrorCodes.RateLimited or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden);
                        _suggestionUnknown = retain;
                        _pendingSuggestion = retain ? request : null;
                        ResultSummary = result.Message;
                        _messageService.ShowError("推荐采购供应商未全部保存", result.Message);
                        NotifySuggestionState();
                        return;
                    }

                    row.AcceptSuggestedSupplierUpdate(result.Data);
                    _pendingSuggestion = null;
                    _suggestionUnknown = false;
                    NotifySuggestionState();
                }
                allSaved = true;
            }
            finally
            {
                _suggestionInFlight = false;
                NotifySuggestionState();
            }
        }, "正在保存推荐采购供应商...");

        if (!allSaved)
        {
            return;
        }

        _messageService.ShowSuccess("推荐采购供应商已保存",
            $"已保存 {dirtyRows.Length} 条建议；既有采购单及其实际送达方未被修改。");
        await LoadPageAsync();
    }

    /// <summary>撤销当前页全部未保存的推荐采购供应商修改。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelSuggestedSupplierChanges))]
    private void CancelSuggestedSupplierChanges()
    {
        if (_suggestionUnknown) return;
        foreach (var row in Items.Where(item => item.IsSuggestionDirty))
        {
            row.RejectSuggestedSupplierChanges();
        }

        ResultSummary = "已还原未保存的推荐采购供应商修改。";
        UpdateSuggestionCommands();
    }

    [RelayCommand(CanExecute = nameof(CanPageChange))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (!await EnsureCanDiscardSuggestionChangesAsync("翻页将放弃未保存的推荐供应商修改。"))
        {
            return;
        }

        _openRequest = null;
        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        await LoadPageAsync();
    }

    #region 来源导航与独立筛选

    // 左栏只使用来源类型，避免顶部复杂筛选悄然改变来源单列表口径。

    [RelayCommand(CanExecute = nameof(CanSelectSourceGroup))]
    private async Task SelectSourceGroupAsync(NetDemandSourceGroupModel? group)
    {
        if (group is null)
        {
            RestoreAppliedSourceSelection();
            return;
        }

        if (group.SourceUid == _appliedSourceUid)
        {
            return;
        }

        if (!await EnsureCanDiscardSuggestionChangesAsync("切换来源将放弃未保存的推荐供应商修改。"))
        {
            RestoreAppliedSourceSelection();
            return;
        }

        _appliedSourceUid = group.SourceUid;
        SelectedSourceGroup = group;
        NotifySourceModeChanged();
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ToggleSourcePanelAsync()
    {
        if (!await EnsureCanDiscardSuggestionChangesAsync("切换来源导航将重新加载页面。"))
        {
            return;
        }
        if (IsSourcePanelOpen)
        {
            CancelSourceGroupLoading();
            IsSourcePanelOpen = false;
            ExitSourceMode();
            await LoadPageAsync();
            return;
        }

        IsSourcePanelOpen = true;
        await RefreshSourceNavigationAsync(reloadCurrentData: false);
    }

    [RelayCommand]
    private async Task RefreshSourceGroupsAsync()
    {
        if (_suggestionUnknown || _suggestionInFlight) return;
        if (!IsSourcePanelOpen)
        {
            return;
        }

        if (!await EnsureCanDiscardSuggestionChangesAsync("刷新来源导航将重新加载当前来源。"))
        {
            return;
        }

        await RefreshSourceNavigationAsync(reloadCurrentData: IsSourceSelected);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearSourceFiltersAsync()
    {
        if (_suggestionUnknown || _suggestionInFlight) return;
        _syncingSourceFilters = true;
        try
        {
            SourceSourceTypes.Clear();
        }
        finally
        {
            _syncingSourceFilters = false;
        }

        await RefreshSourceNavigationAsync(reloadCurrentData: IsSourceSelected);
    }

    #endregion

    private async Task LoadPageAsync(CancellationToken externalCancellation = default)
    {
        if (_suggestionUnknown || _suggestionInFlight) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _searchCancellation = cancellation;
        var selectedUid = SelectedItem?.Uid;

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.SearchAsync(CreateSearchRequest(), cancellation.Token),
                    cancellation.Token,
                    "查询净需求失败");
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    ResultSummary = result.Message;
                    return;
                }

                DetachRows();
                Items.Clear();
                foreach (var dto in result.Data.Items)
                {
                    var row = MaterialDemandManagerRowModel.FromDto(dto);
                    row.PropertyChanged += OnRowPropertyChanged;
                    Items.Add(row);
                }

                TotalCount = result.Data.TotalCount;
                PageIndex = IsSourceSelected ? 1 : result.Data.PageIndex <= 0 ? PageIndex : result.Data.PageIndex;
                if (!IsSourceSelected && result.Data.PageSize > 0)
                {
                    PageSize = result.Data.PageSize;
                }
                SelectedItem = selectedUid is null
                    ? Items.FirstOrDefault()
                    : Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Items.FirstOrDefault();
                ClearChecks();
                ResultSummary = IsSourceSelected
                    ? $"当前来源共 {TotalCount} 条物料需求，已全部加载。"
                    : $"共 {TotalCount} 条物料需求，本页 {Items.Count} 条。";
            }, "正在查询净需求...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// 根据当前模式构建主分页查询或来源导航全量查询。
    /// </summary>
    private MaterialDemandManagerSearchRequest CreateSearchRequest()
        => IsSourceSelected ? CreateSelectedSourceSearchRequest() : CreateMainSearchRequest();

    private MaterialDemandManagerSearchRequest CreateMainSearchRequest()
    {
        var request = new MaterialDemandManagerSearchRequest
        {
            Keyword = Keyword.Trim(),
            PageIndex = PageIndex,
            PageSize = PageSize,
            SourceTypes = SelectedSourceTypes.Select(item => item.Value).ToList(),
            SourceNos = SplitLines(SourceNosText),
            Skus = SplitLines(SkusText),
            CustomerSkus = SplitLines(CustomerSkusText),
            SuggestedPurchaseSupplierKeyword = SuggestedSupplierKeyword.Trim(),
            ActualPurchaseSupplierKeyword = ActualSupplierKeyword.Trim(),
            PurchaseOrderNo = PurchaseOrderNoFilter.Trim(),
            LifecycleStatus = SelectedLifecycle?.Value,
            QuickView = SelectedQuickView?.Value ?? "All",
            DemandRequiredDateFrom = DemandRequiredDateFrom,
            DemandRequiredDateTo = DemandRequiredDateTo,
            IsCustomerSupplied = SelectedDemandRole?.Value,
        };

        if (_openRequest?.NetDemandId is Guid netDemandId)
        {
            request.NetDemandIds.Add(netDemandId);
        }

        if (_openRequest?.GrossDemandId is Guid grossDemandId)
        {
            request.GrossDemandIds.Add(grossDemandId);
        }

        if (_openRequest?.SourceUid is Guid sourceUid)
        {
            request.SourceUids.Add(sourceUid);
        }

        return request;
    }

    private MaterialDemandManagerSearchRequest CreateSelectedSourceSearchRequest()
    {
        var request = CreateMainSearchRequest();
        request.SourceUid = _appliedSourceUid;
        request.PageIndex = 1;
        if (SelectedSourceGroup is not null)
        {
            request.SourceTypes.Add(SelectedSourceGroup.SourceType);
        }

        return request;
    }

    private MaterialDemandSourceGroupSearchRequest CreateSourceGroupSearchRequest() => new()
    {
        SourceTypes = SourceSourceTypes.Select(item => item.Value).ToList(),
        LifecycleStatus = SelectedLifecycle?.Value,
    };

    /// <summary>
    /// 刷新来源列表，并在当前来源仍有效时选择性重载主表；较早请求会被取消。
    /// </summary>
    private async Task<bool> RefreshSourceNavigationAsync(bool reloadCurrentData)
    {
        CancelSourceGroupLoading();
        var cancellation = new CancellationTokenSource();
        _sourceGroupCancellation = cancellation;
        IsSourceGroupLoading = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.SearchSourceGroupsAsync(CreateSourceGroupSearchRequest(), cancellation.Token),
                cancellation.Token,
                "查询净需求来源分组失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_sourceGroupCancellation, cancellation))
            {
                return false;
            }

            if (!result.Success || result.Data is null)
            {
                SourceGroupSummary = result.Message;
                return false;
            }

            var groups = result.Data.Select(NetDemandSourceGroupModel.FromDto).ToArray();
            SourceGroups.Clear();
            foreach (var group in groups)
            {
                SourceGroups.Add(group);
            }

            SourceGroupSummary = $"共 {SourceGroups.Count} 张来源单，{result.Data.Sum(item => item.DemandCount)} 条净需求。";
            if (_appliedSourceUid.HasValue)
            {
                var selected = SourceGroups.FirstOrDefault(item => item.SourceUid == _appliedSourceUid.Value);
                if (selected is null)
                {
                    ExitSourceMode();
                }
                else
                {
                    SelectedSourceGroup = selected;
                }
            }

            if (reloadCurrentData)
            {
                await LoadPageAsync();
            }

            return true;
        }
        finally
        {
            if (ReferenceEquals(_sourceGroupCancellation, cancellation))
            {
                _sourceGroupCancellation = null;
                IsSourceGroupLoading = false;
            }

            cancellation.Dispose();
        }
    }

    private void RestoreAppliedSourceSelection()
    {
        SelectedSourceGroup = _appliedSourceUid.HasValue
            ? SourceGroups.FirstOrDefault(item => item.SourceUid == _appliedSourceUid.Value)
            : null;
    }

    /// <summary>
    /// 对来源筛选变化做短防抖，避免多选控件连续变更触发重复请求。
    /// </summary>
    private async void OnSourceGroupFilterChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingSourceFilters || !IsSourcePanelOpen)
        {
            return;
        }

        _sourceGroupFilterDebounceCancellation?.Cancel();
        _sourceGroupFilterDebounceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _sourceGroupFilterDebounceCancellation = cancellation;
        try
        {
            await Task.Delay(250, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                await RefreshSourceGroupsCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_sourceGroupFilterDebounceCancellation, cancellation))
            {
                _sourceGroupFilterDebounceCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task RefreshManagerAsync()
    {
        if (IsSourcePanelOpen && !await RefreshSourceNavigationAsync(reloadCurrentData: false))
        {
            return;
        }

        await LoadPageAsync();
    }

    /// <summary>在会丢弃当前页数据的动作前统一取得用户确认。</summary>
    private async Task<bool> EnsureCanDiscardSuggestionChangesAsync(string action)
    {
        if (_suggestionUnknown || _suggestionInFlight) return false;
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃推荐供应商修改",
            $"{action} 当前未保存修改将丢失，是否继续？",
            "继续");
    }

    private void ExitSourceMode()
    {
        _appliedSourceUid = null;
        SelectedSourceGroup = null;
        PageIndex = 1;
        NotifySourceModeChanged();
    }

    private void NotifySourceModeChanged()
    {
        OnPropertyChanged(nameof(IsSourceSelected));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageStatus));
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    private void CancelSourceGroupLoading()
    {
        _sourceGroupFilterDebounceCancellation?.Cancel();
        _sourceGroupFilterDebounceCancellation?.Dispose();
        _sourceGroupFilterDebounceCancellation = null;
        _sourceGroupCancellation?.Cancel();
        _sourceGroupCancellation?.Dispose();
        _sourceGroupCancellation = null;
        IsSourceGroupLoading = false;
    }

    private static List<string> SplitLines(string value) => value
        .Split(['\r', '\n', ',', ';', '；', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void InitializeDefaults()
    {
        SelectedDemandRole ??= DemandRoleOptions[0];
        SelectedLifecycle ??= LifecycleOptions[0];
        SelectedQuickView ??= QuickViewOptions[0];
    }

    partial void OnIsAllCheckedChanged(bool? value)
    {
        if (_syncingChecks || value is null)
        {
            return;
        }

        _syncingChecks = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsChecked = value.Value;
            }
        }
        finally
        {
            _syncingChecks = false;
            UpdateSelectionCommands();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialDemandManagerRowModel.IsSuggestionDirty))
        {
            UpdateSuggestionCommands();
        }

        if (e.PropertyName != nameof(MaterialDemandManagerRowModel.IsChecked) || _syncingChecks)
        {
            return;
        }

        _syncingChecks = true;
        try
        {
            if (sender is MaterialDemandManagerRowModel changedRow)
            {
                // 当前页只要加载了同一拆解树的父子行，就必须保持一致选择；同步期间忽略嵌套通知以避免递归。
                foreach (var relatedRow in Items.Where(item => IsSameLoadedDemandFamily(item, changedRow)))
                {
                    relatedRow.IsChecked = changedRow.IsChecked;
                }
            }

            UpdateAllCheckedState();
        }
        finally
        {
            _syncingChecks = false;
        }

        UpdateSelectionCommands();
    }

    private void ClearChecks()
    {
        _syncingChecks = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsChecked = false;
            }

            IsAllChecked = false;
        }
        finally
        {
            _syncingChecks = false;
        }
        UpdateSelectionCommands();
    }

    /// <summary>判断两行是否属于当前加载集合中的同一来源拆解树。</summary>
    private static bool IsSameLoadedDemandFamily(
        MaterialDemandManagerRowModel left,
        MaterialDemandManagerRowModel right)
        => left.GrossDemandId == right.GrossDemandId
           && left.DecompositionRevision == right.DecompositionRevision
           && left.RootNetDemandId == right.RootNetDemandId;

    /// <summary>根据当前页实际选中数刷新表头三态，不触发批量选择回写。</summary>
    private void UpdateAllCheckedState()
    {
        var checkedCount = CheckedCount;
        IsAllChecked = Items.Count == 0 || checkedCount == 0
            ? false
            : checkedCount == Items.Count ? true : null;
    }

    private void UpdateSelectionCommands()
    {
        OnPropertyChanged(nameof(CheckedCount));
        RollbackSourceEstablishmentCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy && !_suggestionUnknown;
    private bool CanWriteCommand() => CanWrite && IsNotBusy;
    private bool CanSaveSuggestedSuppliers() => CanWrite && IsNotBusy
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage)
        && HasUnsavedChanges;
    private bool CanCancelSuggestedSupplierChanges() => IsNotBusy && !_suggestionUnknown && HasUnsavedChanges;
    private bool CanRollbackSourceEstablishment() => CanWrite && IsNotBusy
        && Items.Any(item => item.IsChecked)
        && Items.Where(item => item.IsChecked).All(item => item.LifecycleStatus == NetDemandLifecycleStatusEnum.Active)
        && Items.Where(item => item.IsChecked)
            .GroupBy(item => item.GrossDemandId)
            .All(group => group.Select(item => item.DecompositionRevision).Distinct().Count() == 1);
    private bool CanPageChange() => CanRun() && !IsSourceSelected;
    private bool CanSelectSourceGroup(NetDemandSourceGroupModel? group) => CanRun() && !IsSourceGroupLoading && group is not null;

    private void UpdateSuggestionCommands()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSuggestedSuppliersCommand.NotifyCanExecuteChanged();
        CancelSuggestedSupplierChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>同步通知未决关闭门禁、建议列只读状态和原请求重试入口。</summary>
    private void NotifySuggestionState()
    {
        OnPropertyChanged(nameof(HasPendingSuggestion));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEditSuggestion));
        OnPropertyChanged(nameof(IsSuggestionReadOnly));
        SaveSuggestedSuppliersCommand.NotifyCanExecuteChanged();
        CancelSuggestedSupplierChangesCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    private void DetachRows()
    {
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        try
        {
            _supplierSearchCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 搜索请求拥有其 CTS；释放与页面关闭竞态时保持 Dispose 幂等。
        }
        CancelSourceGroupLoading();
        DetachRows();
        SourceSourceTypes.CollectionChanged -= OnSourceGroupFilterChanged;
    }
}
