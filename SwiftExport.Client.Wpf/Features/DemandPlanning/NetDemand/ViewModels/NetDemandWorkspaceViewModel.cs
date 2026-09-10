using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
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
public sealed partial class NetDemandWorkspaceViewModel : ViewModelBaseEx,
    IAsyncInitializable,
    IDocumentTabViewModel,
    IDocumentTabInitializable,
    IDisposable
{
    public const string RouteKey = "net-demands";

    private readonly IMaterialDemandWorkspaceApi _api;
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
    private NetDemandWorkspaceOpenRequest? _openRequest;

    public NetDemandWorkspaceViewModel(
        IMaterialDemandWorkspaceApi api,
        IProcurementMasterDataQueryApi masterDataApi,
        IApiCallRunner apiCallRunner,
        IDialogService dialogService,
        IMessageService messageService,
        INetDemandDialogService netDemandDialogService)
    {
        _api = api;
        _masterDataApi = masterDataApi;
        _apiCallRunner = apiCallRunner;
        _dialogService = dialogService;
        _messageService = messageService;
        _netDemandDialogService = netDemandDialogService;
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

    public ObservableCollection<MaterialDemandWorkspaceRowModel> Items { get; } = [];
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

    public ObservableCollection<NetDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    /// <summary>
    /// 来源导航独立使用的来源类型筛选。
    /// </summary>
    public ObservableCollection<NetDemandFilterOption<string>> SourceSourceTypes { get; } = [];
    public string DocumentTitle => "物料需求";
    public bool HasUnsavedChanges => Items.Any(item => item.IsSuggestionDirty);
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
    public partial MaterialDemandWorkspaceRowModel? SelectedItem { get; set; }

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
        _openRequest = parameter as NetDemandWorkspaceOpenRequest;
        if (!string.IsNullOrWhiteSpace(_openRequest?.SourceNo))
        {
            SourceNosText = _openRequest.SourceNo;
        }

        await LoadPageAsync(cancellationToken);
    }

    /// <summary>写命令执行中拒绝关闭；存在建议编辑时由用户明确决定是否放弃。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
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
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardSuggestionChangesAsync("刷新将放弃未保存的推荐供应商修改。"))
        {
            return;
        }
        await RefreshWorkspaceAsync();
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
        DemandRequiredDateFrom = null;
        DemandRequiredDateTo = null;
        SelectedDemandRole = DemandRoleOptions[0];
        SelectedSourceTypes.Clear();
        PageIndex = 1;
        await LoadPageAsync();
    }

    /// <summary>打开来源需求候选；建立由对话框完成，关闭后刷新物料工作台。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private async Task EstablishFromSourcesAsync()
    {
        var hasEstablished = await _netDemandDialogService.ShowSourceDemandCandidatesAsync();
        if (hasEstablished)
        {
            await RefreshWorkspaceAsync();
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
                "回退来源物料需求", $"将回退 {items.Count} 条来源需求的当前物料修订，是否继续？", "安全回退")) return;
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.RollbackSourceEstablishmentAsync(new RollbackMaterialDemandsCommand
                {
                    CommandId = Guid.NewGuid(), Reason = "用户从物料需求工作台安全回退", Items = items
                }),
                "回退来源物料需求失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("回退失败", result.Message);
                return;
            }
            _messageService.ShowSuccess("回退完成",
                $"已回退 {result.Data.RolledBackSourceCount} 条，已处于回退状态 {result.Data.AlreadyRolledBackCount} 条。");
            await RefreshWorkspaceAsync();
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
        var dirtyRows = Items.Where(item => item.IsSuggestionDirty).ToArray();
        var allSaved = false;
        await RunBusyAsync(async () =>
        {
            foreach (var row in dirtyRows)
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.UpdateSuggestedPurchaseSupplierAsync(new UpdateSuggestedPurchaseSupplierCommand
                    {
                        NetDemandId = row.Uid,
                        Version = row.Version,
                        SuggestedPurchaseSupplierId = row.SuggestedPurchaseSupplierId
                    }),
                    "保存推荐采购供应商失败");
                if (!result.Success || result.Data is null)
                {
                    ResultSummary = result.Message;
                    _messageService.ShowError("推荐采购供应商未全部保存", result.Message);
                    UpdateSuggestionCommands();
                    return;
                }

                row.AcceptSuggestedSupplierUpdate(result.Data);
            }

            allSaved = true;
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
                    var row = MaterialDemandWorkspaceRowModel.FromDto(dto);
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
    private MaterialDemandWorkspaceSearchRequest CreateSearchRequest()
        => IsSourceSelected ? CreateSelectedSourceSearchRequest() : CreateMainSearchRequest();

    private MaterialDemandWorkspaceSearchRequest CreateMainSearchRequest()
    {
        var request = new MaterialDemandWorkspaceSearchRequest
        {
            Keyword = Keyword.Trim(),
            PageIndex = PageIndex,
            PageSize = PageSize,
            SourceTypes = SelectedSourceTypes.Select(item => item.Value).ToList(),
            SourceNos = SplitLines(SourceNosText),
            Skus = SplitLines(SkusText),
            CustomerSkus = SplitLines(CustomerSkusText),
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

    private MaterialDemandWorkspaceSearchRequest CreateSelectedSourceSearchRequest()
    {
        var request = new MaterialDemandWorkspaceSearchRequest
        {
            SourceUid = _appliedSourceUid,
            PageIndex = 1,
            PageSize = PageSize,
        };
        if (SelectedSourceGroup is not null)
        {
            request.SourceTypes.Add(SelectedSourceGroup.SourceType);
        }

        return request;
    }

    private MaterialDemandSourceGroupSearchRequest CreateSourceGroupSearchRequest() => new()
    {
        SourceTypes = SourceSourceTypes.Select(item => item.Value).ToList(),
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

    private async Task RefreshWorkspaceAsync()
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
        if (e.PropertyName == nameof(MaterialDemandWorkspaceRowModel.IsSuggestionDirty))
        {
            UpdateSuggestionCommands();
        }

        if (e.PropertyName != nameof(MaterialDemandWorkspaceRowModel.IsChecked) || _syncingChecks)
        {
            return;
        }

        _syncingChecks = true;
        try
        {
            var checkedCount = CheckedCount;
            IsAllChecked = Items.Count == 0 || checkedCount == 0
                ? false
                : checkedCount == Items.Count ? true : null;
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
        foreach (var item in Items)
        {
            item.IsChecked = false;
        }

        IsAllChecked = false;
        _syncingChecks = false;
        UpdateSelectionCommands();
    }

    private void UpdateSelectionCommands()
    {
        OnPropertyChanged(nameof(CheckedCount));
        RollbackSourceEstablishmentCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanWriteCommand() => CanWrite && IsNotBusy;
    private bool CanSaveSuggestedSuppliers() => CanWrite && IsNotBusy && HasUnsavedChanges;
    private bool CanCancelSuggestedSupplierChanges() => IsNotBusy && HasUnsavedChanges;
    private bool CanRollbackSourceEstablishment() => CanWrite && IsNotBusy
        && Items.Any(item => item.IsChecked)
        && Items.Where(item => item.IsChecked)
            .GroupBy(item => item.GrossDemandId)
            .All(group => group.Select(item => item.DecompositionRevision).Distinct().Count() == 1);
    private bool CanPageChange() => IsNotBusy && !IsSourceSelected;
    private bool CanSelectSourceGroup(NetDemandSourceGroupModel? group) => IsNotBusy && !IsSourceGroupLoading && group is not null;

    private void UpdateSuggestionCommands()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSuggestedSuppliersCommand.NotifyCanExecuteChanged();
        CancelSuggestedSupplierChangesCommand.NotifyCanExecuteChanged();
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
