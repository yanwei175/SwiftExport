using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

/// <summary>查询成品需求本体事实，维护独立需求，并按现有权限导航来源和订单跟踪明细。</summary>
public sealed partial class GrossDemandWorkspaceViewModel : ViewModelBaseEx,
    IAsyncInitializable,
    IDocumentTabViewModel,
    IDisposable
{
    public const string RouteKey = "gross-demands";

    private readonly IGrossDemandWorkspaceApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _sourceGroupCancellation;
    private CancellationTokenSource? _sourceGroupFilterDebounceCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _customerCancellation;
    private string? _pendingCloseUserId;
    private bool _initialized;
    private GrossDemandQuerySnapshot? _appliedQuery;
    private GrossDemandQuerySnapshot? _appliedMainQuery;
    private GrossDemandSourceGroupModel? _appliedSourceGroup;
    private bool _syncingSourceFilters;
    private GrossDemandLifecycleStatusEnum[] _appliedSourceStatuses = [GrossDemandLifecycleStatusEnum.Active];
    private bool _disposed;
    private Guid? _appliedSourceUid;
    private CloseGrossDemandCommand? _pendingClose;
    private bool _closeUnknown;
    private readonly ICurrentUserService _currentUser;

    public GrossDemandWorkspaceViewModel(
        IGrossDemandWorkspaceApi api,
        IApiCallRunner apiCallRunner,
        INavigationService navigationService,
        IDialogService dialogService,
        IMessageService messageService,
        GrossDemandManualEditorViewModel manualEditor,
        ICurrentUserService currentUser)
    {
        _api = api;
        _apiCallRunner = apiCallRunner;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _messageService = messageService;
        _currentUser = currentUser;
        ManualEditor = manualEditor;
        Title = "成品需求";
        ManualEditor.PropertyChanged += OnManualEditorPropertyChanged;
        ManualEditor.Saved += OnManualSaved;

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(CreateManualCommand);
        RegisterBusyAwareCommand(EditSelectedManualCommand);
        RegisterBusyAwareCommand(CloseSelectedManualCommand);
        RegisterBusyAwareCommand(OpenSourceCommand);
        RegisterBusyAwareCommand(OpenTraceCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(SelectSourceGroupCommand);
        RegisterBusyAwareCommand(ToggleSourcePanelCommand);
        RegisterBusyAwareCommand(ClearSourceFiltersCommand);
        RegisterBusyAwareCommand(ReturnToAllCommand);
        RegisterBusyAwareCommand(ApplyMoreFiltersCommand);
        RegisterBusyAwareCommand(RefreshCustomerOptionsCommand);

        SourceLifecycleStatuses.Add(LifecycleStatusOptions[0]);
        SourceLifecycleStatuses.CollectionChanged += OnSourceGroupFilterChanged;
        SelectedCustomers.CollectionChanged += OnDraftCollectionChanged;
        SelectedSourceTypes.CollectionChanged += OnDraftCollectionChanged;
        SelectedLifecycleStatuses.CollectionChanged += OnDraftCollectionChanged;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
    }

    public GrossDemandManualEditorViewModel ManualEditor { get; }
    public ObservableCollection<GrossDemandRowModel> Items { get; } = [];
    public ObservableCollection<GrossDemandSourceGroupModel> SourceGroups { get; } = [];

    public IReadOnlyList<GrossDemandFilterOption<string>> SourceTypeOptions { get; } =
    [
        new("SO", "销售订单"),
        new("MD", "手工需求"),
        new("CR", "投诉补货"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> LifecycleStatusOptions { get; } =
    [
        new(GrossDemandLifecycleStatusEnum.Active, "活动"),
        new(GrossDemandLifecycleStatusEnum.Closed, "已关闭"),
        new(GrossDemandLifecycleStatusEnum.Voided, "已作废"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<bool?>> StandardTypeOptions { get; } =
    [
        new(null, "全部类型"),
        new(false, "标准件"),
        new(true, "非标件"),
    ];

    public ObservableCollection<GrossDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> SelectedLifecycleStatuses { get; } =
        [
        new(GrossDemandLifecycleStatusEnum.Active, "活动")

        ];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> SourceLifecycleStatuses { get; } = [];

    public string DocumentTitle => "成品需求";
    public bool HasUnsavedChanges => ManualEditor.HasUnsavedChanges || ManualEditor.HasPendingSave || _closeUnknown;
    /// <summary>关闭结果未知时保留原来源、版本、原因和命令身份。</summary>
    public bool HasPendingClose => _closeUnknown;
    public int TotalPages => IsSourceSelected || TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => IsSourceSelected ? $"当前来源共 {TotalCount} 条" : $"第 {PageIndex} / {TotalPages} 页";
    /// <summary>客户候选由工作区只读接口提供；当前选择仅在查询时应用。</summary>
    public ObservableCollection<GrossDemandCustomerOption> CustomerOptions { get; } = [];
    /// <summary>精确客户多选，发送稳定身份而非代码或名称关键词。</summary>
    public ObservableCollection<GrossDemandCustomerOption> SelectedCustomers { get; } = [];
    [ObservableProperty] public partial string? CustomerLoadError { get; private set; }
    [ObservableProperty] public partial bool HasUnappliedChanges { get; private set; }
    /// <summary>来源模式禁用不会生效的主区域条件。</summary>
    public bool CanEditMainFilters => !IsSourceSelected;
    /// <summary>当前模式与已应用查询的说明。</summary>
    public string QueryModeText => IsSourceSelected ? $"来源模式：{_appliedSourceGroup?.SourceNo}，全量加载；仅左侧来源状态生效，主区域条件不生效。"
        : HasUnappliedChanges ? "条件已修改，尚未应用；刷新和翻页仍使用已应用查询。" : "全部需求模式：普通分页；表头筛选只作用于当前加载数据。";
    public bool IsSourceSelected => _appliedSourceUid.HasValue;

    [ObservableProperty]
    public partial bool IsSourcePanelOpen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectSourceGroupCommand))]
    public partial bool IsSourceGroupLoading { get; private set; }

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

    [ObservableProperty]
    public partial bool IsMultiLineFilterOpen { get; set; }

    [ObservableProperty]
    public partial DateTime? RequiredDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? RequiredDateTo { get; set; }

    [ObservableProperty]
    public partial GrossDemandFilterOption<bool?>? SelectedStandardType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceSelected))]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    [NotifyCanExecuteChangedFor(nameof(PageChangeCommand))]
    public partial GrossDemandSourceGroupModel? SelectedSourceGroup { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    public partial GrossDemandRowModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial object? ActiveDrawerContent { get; private set; }

    [ObservableProperty]
    public partial bool IsDrawerOpen { get; private set; }

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
    public partial string ResultSummary { get; set; } = "尚未加载成品需求。";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SelectedStandardType = StandardTypeOptions[0];

        await Task.WhenAll(LoadPageAsync(), LoadCustomerOptionsAsync());
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (_closeUnknown || ManualEditor.IsEditorLocked || IsBusy) return false;
        return await ConfirmDiscardManualChangesAsync(cancellationToken);
    }

    #region 主区域筛选与分页

    // 主区域条件只负责普通分页查询；执行主查询时会退出来源单模式。
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        if (query is not null)
        {
            Keyword = query;
        }

        await LoadPageAsync(CaptureMainQuery(), 1, PageSize);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (await ConfirmDiscardManualChangesAsync())
        {
            await Task.WhenAll(RefreshManagerAsync(), LoadCustomerOptionsAsync());
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        Keyword = string.Empty;
        SelectedCustomers.Clear();

        SourceNosText = string.Empty;
        SkusText = string.Empty;
        CustomerSkusText = string.Empty;
        RequiredDateFrom = null;
        RequiredDateTo = null;
        SelectedStandardType = StandardTypeOptions[0];
        SelectedSourceTypes.Clear();
        SelectedLifecycleStatuses.Clear();
        await LoadPageAsync(CaptureMainQuery(), 1, PageSize);
    }

    #endregion

    #region 成品需求业务命令

    [RelayCommand(CanExecute = nameof(CanCreateManual))]
    private async Task CreateManualAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        ManualEditor.OpenCreate();
        ActiveDrawerContent = ManualEditor;
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedManual))]
    private async Task EditSelectedManualAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.GetDetailAsync(selected.Uid, cancellation.Token),
                cancellation.Token,
                "加载手工成品需求失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cancellation)
                || ManualEditor.IsEditorLocked)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("无法编辑手工成品需求", result.Message);
                return;
            }

            ManualEditor.OpenEdit(result.Data);
            ActiveDrawerContent = ManualEditor;
            IsDrawerOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_detailCancellation, cancellation))
            {
                _detailCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseSelectedManual))]
    private async Task CloseSelectedManualAsync()
    {
        var selected = SelectedItem;
        var closeUser = _currentUser.CurrentUser;
        string? reason = null;
        if (!_closeUnknown)
        {
            if (selected is null || !await ConfirmDiscardManualChangesAsync()) return;
            var input = new GrossDemandCloseReasonViewModel(selected.SourceNo);
            var response = await _dialogService.ShowAsync(input,
                new DialogOptions("关闭独立需求") { PrimaryButtonText = "确认关闭", CloseButtonText = "取消", Width = 560 }, input);
            if (response != DialogResult.Primary || !input.IsPrimaryButtonEnabled || !CanCloseSelectedManual() || !ReferenceEquals(closeUser, _currentUser.CurrentUser)) return;
            reason = input.Reason.Trim();
        }
        await RunWriteAsync(async () =>
        {
            if (!_closeUnknown)
            {
                _pendingCloseUserId = closeUser?.UserId;
                _pendingClose = new CloseGrossDemandCommand
                {
                    CommandId = Guid.NewGuid(),
                    Uid = selected!.Uid,
                    Version = selected.Version,
                    Reason = reason!
                };
            }
            var result = await _apiCallRunner.RunAsync(
                () => _api.CloseManualAsync(_pendingClose!.Uid, _pendingClose),
                "关闭手工成品需求失败");
            if (!result.Success)
            {
                var code = result.Error?.Code;
                _closeUnknown = code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable
                    || (_closeUnknown && code is ServiceErrorCodes.RateLimited or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden);
                if (!_closeUnknown) _pendingClose = null;
                OnPropertyChanged(nameof(HasPendingClose));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                CloseSelectedManualCommand.NotifyCanExecuteChanged();
                _messageService.ShowError("关闭失败", result.Message);
                return;
            }

            _closeUnknown = false;
            _pendingClose = null;
            OnPropertyChanged(nameof(HasPendingClose));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            _messageService.ShowSuccess("手工成品需求已关闭", selected?.SourceNo ?? result.Data?.SourceNo ?? string.Empty);
            await ReloadAfterWriteAsync();
        }, "正在关闭手工成品需求...");
    }

    #endregion

    #region 抽屉与来源追溯

    /// <summary>用成品需求 Uid 打开独立只读追踪，页面重新加载当前下游证据。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTracking))]
    private async Task OpenTraceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync()) return;
        await _navigationService.OpenDocumentAsync(
            DemandTrackingDetailViewModel.RouteKey, selected.Uid.ToString("N"), selected.Uid);
    }

    // 来源单据和订单跟踪通过现有导航打开，独立编辑器继续使用抽屉及未保存保护。

    [RelayCommand(CanExecute = nameof(CanOpenSource))]
    private async Task OpenSourceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        if (string.Equals(selected.SourceType, "SO", StringComparison.OrdinalIgnoreCase))
        {
            await _navigationService.OpenDocumentAsync(
                SalesOrderManagerViewModel.DetailRouteKey,
                $"sales-order:{selected.SourceUid:N}",
                new SalesOrderDocumentOpenRequest(selected.SourceUid));
            return;
        }

        if (string.Equals(selected.SourceType, "CR", StringComparison.OrdinalIgnoreCase))
        {
            await _navigationService.OpenDocumentAsync(AfterSalesDetailViewModel.RouteKey,
                $"after-sales:{selected.SourceUid:N}", new AfterSalesDocumentOpenRequest { AfterSalesId = selected.SourceUid });
            return;
        }
        if (string.Equals(selected.SourceType, "MD", StringComparison.OrdinalIgnoreCase))
        {
            if (CanEditSelectedManual())
            {
                await EditSelectedManualAsync();
            }
            else
            {
                _messageService.ShowInfo("来源导航", "该手工来源当前不可编辑，请在成品需求列表查看保留的来源快照。");
            }

            return;
        }

        _messageService.ShowInfo("来源导航", "该来源类型尚未接入独立单据页面，请在成品需求列表查看保留的来源快照。");
    }

    [RelayCommand]
    private async Task CloseDrawerAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        ManualEditor.Close(discardChanges: true);
        ActiveDrawerContent = null;
        IsDrawerOpen = false;
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanPageChange))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        await LoadPageAsync(_appliedQuery, request.IsPageSizeChanged ? 1 : request.NewIndex, request.NewSize);
    }

    #region 来源导航与独立筛选

    // 左栏筛选拥有独立状态和请求，不读取主区域关键词、日期或物料条件。

    [RelayCommand(CanExecute = nameof(CanSelectSourceGroup))]
    private async Task SelectSourceGroupAsync(GrossDemandSourceGroupModel? group)
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

        if (!await ConfirmDiscardManualChangesAsync())
        {
            RestoreAppliedSourceSelection();
            return;
        }

        if (!await LoadPageAsync(CaptureSourceQuery(group.SourceUid), 1, PageSize, group)) RestoreAppliedSourceSelection();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ToggleSourcePanelAsync()
    {
        if (IsSourcePanelOpen)
        {
            if (!await ConfirmDiscardManualChangesAsync())
            {
                return;
            }

            CancelSourceGroupLoading();
            IsSourcePanelOpen = false;
            await ReturnToAllAsync();
            return;
        }

        IsSourcePanelOpen = true;
        await RefreshSourceNavigationAsync(reloadCurrentData: false);
    }

    [RelayCommand]
    private async Task RefreshSourceGroupsAsync()
    {
        if (_closeUnknown || ManualEditor.HasPendingSave) return;
        if (IsSourcePanelOpen)
        {
            if (!await ConfirmDiscardManualChangesAsync()) { RestoreSourceStatusInput(); return; }
            await RefreshSourceNavigationAsync(reloadCurrentData: IsSourceSelected);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearSourceFiltersAsync()
    {
        if (_closeUnknown || ManualEditor.HasPendingSave) return;
        if (!await ConfirmDiscardManualChangesAsync()) return;
        _syncingSourceFilters = true;
        try
        {
            SourceLifecycleStatuses.Clear();
        }
        finally
        {
            _syncingSourceFilters = false;
        }

        await RefreshSourceNavigationAsync(reloadCurrentData: IsSourceSelected);
    }

    #endregion

    #region 查询加载与并发控制

    // 主表和来源列表分别取消旧请求，确保较晚操作始终获胜。

    /// <summary>成功后才接纳查询、页码和来源模式；失败始终保留上一批事实及其解释。</summary>
    private async Task<bool> LoadPageAsync(GrossDemandQuerySnapshot? candidate = null, int? page = null, int? size = null, GrossDemandSourceGroupModel? sourceGroup = null)
    {
        if (_disposed || _closeUnknown || ManualEditor.HasPendingSave || !_currentUser.HasPermission(PermissionKeys.DemandPlanningSourceView)) return false;
        var query = candidate ?? _appliedQuery ?? CaptureMainQuery();
        if (query.RequiredDateFrom > query.RequiredDateTo) { ResultSummary = "要求交期开始日期不能晚于结束日期；原查询结果保留。"; return false; }
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _searchCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser; var request = query.ForPage(page ?? PageIndex, size ?? PageSize);
        var selectedUid = SelectedItem?.Uid; var accepted = false;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(() => _api.SearchAsync(request, token), token, "查询成品需求失败");
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation) || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
                if (!result.Success || result.Data is null) { ResultSummary = result.Message + "；原查询结果保留。"; return; }
                Items.Clear(); foreach (var dto in result.Data.Items) Items.Add(GrossDemandRowModel.FromDto(dto));
                _appliedQuery = query; _appliedSourceUid = query.SourceUid;
                if (!query.SourceUid.HasValue) _appliedMainQuery = query;
                _appliedSourceGroup = query.SourceUid.HasValue ? sourceGroup ?? SourceGroups.FirstOrDefault(group => group.SourceUid == query.SourceUid) ?? _appliedSourceGroup : null;
                SelectedSourceGroup = _appliedSourceGroup;
                TotalCount = result.Data.TotalCount; PageIndex = query.SourceUid.HasValue ? 1 : request.PageIndex; PageSize = request.PageSize;
                SelectedItem = Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Items.FirstOrDefault();
                NotifyQueryMode();
                ResultSummary = IsSourceSelected ? $"当前来源共 {TotalCount} 条成品需求，已全部加载；列筛选仅过滤已加载数据。" : $"共 {TotalCount} 条成品需求，本页 {Items.Count} 条；列筛选仅过滤本页。";
                accepted = true;
            }, "正在查询成品需求...", skipIfBusy: false);
            return accepted;
        }
        finally { if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null; }
    }
    /// <summary>冻结主区域输入，客户按身份精确匹配，文本批量条件只在应用时提交。</summary>
    private GrossDemandQuerySnapshot CaptureMainQuery() => new(Keyword.Trim(), SelectedSourceTypes.Select(item => item.Value).ToArray(), SplitLines(SourceNosText).ToArray(), SplitLines(SkusText).ToArray(),
        SplitLines(CustomerSkusText).ToArray(), SelectedCustomers.Select(item => item.Id).ToArray(), SelectedLifecycleStatuses.Select(item => item.Value).ToArray(), RequiredDateFrom?.Date, RequiredDateTo?.Date, SelectedStandardType?.Value);
    private GrossDemandQuerySnapshot CaptureSourceQuery(Guid uid, IEnumerable<GrossDemandLifecycleStatusEnum>? statuses = null) => new(string.Empty, [], [], [], [], [], (statuses ?? SourceLifecycleStatuses.Select(item => item.Value)).ToArray(), null, null, null, uid);
    /// <summary>离开来源模式时恢复上次普通分页查询，不静默应用先前编辑的条件。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ReturnToAllAsync()
    {
        if (await ConfirmDiscardManualChangesAsync()) await LoadPageAsync(_appliedMainQuery ?? CaptureMainQuery(), 1, PageSize);
    }
    /// <summary>更多条件明确应用后关闭浮层。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyMoreFiltersAsync() { await SearchAsync(null); IsMultiLineFilterOpen = false; }
    /// <summary>清空更多条件只修改输入，等待明确应用。</summary>
    [RelayCommand]
    private void ClearMoreFilters() { SourceNosText = SkusText = CustomerSkusText = string.Empty; RequiredDateFrom = RequiredDateTo = null; }
    /// <summary>收起不丢失尚未应用的批量文本。</summary>
    [RelayCommand]
    private void CollapseMoreFilters() => IsMultiLineFilterOpen = false;
    private void OnDraftCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyQueryMode();
    partial void OnKeywordChanged(string value) => NotifyQueryMode();
    partial void OnSourceNosTextChanged(string value) => NotifyQueryMode();
    partial void OnSkusTextChanged(string value) => NotifyQueryMode();
    partial void OnCustomerSkusTextChanged(string value) => NotifyQueryMode();
    partial void OnRequiredDateFromChanged(DateTime? value) => NotifyQueryMode();
    partial void OnRequiredDateToChanged(DateTime? value) => NotifyQueryMode();
    partial void OnSelectedStandardTypeChanged(GrossDemandFilterOption<bool?>? value) => NotifyQueryMode();
    private void NotifyQueryMode()
    {
        HasUnappliedChanges = _appliedMainQuery is not null && !_appliedMainQuery.SameConditions(CaptureMainQuery());
        OnPropertyChanged(nameof(IsSourceSelected)); OnPropertyChanged(nameof(CanEditMainFilters)); OnPropertyChanged(nameof(QueryModeText)); OnPropertyChanged(nameof(TotalPages)); OnPropertyChanged(nameof(PageStatus));
        PageChangeCommand.NotifyCanExecuteChanged();
    }
    /// <summary>会话替换取消旧读取并隐藏旧用户事实，不释放未知命令的重试身份。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        _searchCancellation?.Cancel(); _customerCancellation?.Cancel(); CancelSourceGroupLoading(); _detailCancellation?.Cancel();
        Items.Clear(); SourceGroups.Clear(); CustomerOptions.Clear(); SelectedCustomers.Clear(); SelectedItem = null;
        _appliedQuery = _appliedMainQuery = null; _appliedSourceUid = null; _appliedSourceGroup = null; SelectedSourceGroup = null;
        TotalCount = 0; PageIndex = 1; IsDrawerOpen = false; ResultSummary = "会话已变化，请重新查询。"; NotifyQueryMode();
        SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); OpenSourceCommand.NotifyCanExecuteChanged(); OpenTraceCommand.NotifyCanExecuteChanged();
    }
    private GrossDemandSourceGroupSearchRequest CreateSourceGroupSearchRequest() => new()
    {
        LifecycleStatuses = SourceLifecycleStatuses.Select(item => item.Value).ToList(),
    };

    private async Task<bool> RefreshSourceNavigationAsync(bool reloadCurrentData)
    {
        CancelSourceGroupLoading();
        var cancellation = new CancellationTokenSource();
        _sourceGroupCancellation = cancellation;
        var user = _currentUser.CurrentUser;
        var request = CreateSourceGroupSearchRequest();
        IsSourceGroupLoading = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.SearchSourceGroupsAsync(request, cancellation.Token),
                cancellation.Token,
                "查询成品需求来源分组失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_sourceGroupCancellation, cancellation) || !ReferenceEquals(user, _currentUser.CurrentUser))
            {
                return false;
            }

            if (!result.Success || result.Data is null)
            {
                SourceGroupSummary = result.Message;
                return false;
            }

            var groups = result.Data.Select(GrossDemandSourceGroupModel.FromDto).ToArray();
            _appliedSourceStatuses = request.LifecycleStatuses.ToArray();
            SourceGroups.Clear();
            foreach (var group in groups)
            {
                SourceGroups.Add(group);
            }

            SourceGroupSummary = $"共 {SourceGroups.Count} 张来源单，{result.Data.Sum(item => item.DemandCount)} 条成品需求。";
            if (_appliedSourceUid.HasValue)
            {
                var selected = SourceGroups.FirstOrDefault(item => item.SourceUid == _appliedSourceUid.Value);
                if (selected is null)
                {
                    SourceGroupSummary += " 当前来源不在导航结果中，保留已加载数据，可返回全部需求。";
                }
                else
                {
                    SelectedSourceGroup = selected;
                }
            }

            if (reloadCurrentData)
            {
                if (IsSourceSelected && SourceGroups.All(group => group.SourceUid != _appliedSourceUid))
                    await LoadPageAsync(_appliedMainQuery ?? CaptureMainQuery(), 1, PageSize);
                else await LoadPageAsync(IsSourceSelected ? CaptureSourceQuery(_appliedSourceUid!.Value, request.LifecycleStatuses) : _appliedQuery);
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

    /// <summary>拒绝放弃编辑时恢复已应用来源状态，避免下拉显示与保留数据互相矛盾。</summary>
    private void RestoreSourceStatusInput()
    {
        _syncingSourceFilters = true;
        try
        {
            SourceLifecycleStatuses.Clear();
            foreach (var status in IsSourceSelected ? _appliedQuery!.LifecycleStatuses : _appliedSourceStatuses)
                SourceLifecycleStatuses.Add(LifecycleStatusOptions.First(option => option.Value == status));
        }
        finally { _syncingSourceFilters = false; }
        SourceGroupSummary = "已保留未保存修改及原来源筛选。";
    }

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

    /// <summary>客户候选独立加载，只依赖成品需求查看权限；失败不清空查询结果。</summary>
    private async Task LoadCustomerOptionsAsync()
    {
        if (_disposed || !_currentUser.HasPermission(PermissionKeys.DemandPlanningSourceView)) return;
        _customerCancellation?.Cancel(); using var cancellation = new CancellationTokenSource(); _customerCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser;
        try
        {
            var result = await _apiCallRunner.RunAsync(() => _api.GetCustomerOptionsAsync(token), token, "客户候选加载失败");
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            CustomerLoadError = result.Success ? null : result.Message;
            if (!result.Success || result.Data is null) return;
            var retained = SelectedCustomers.Select(item => item.Id).ToHashSet();
            CustomerOptions.Clear(); foreach (var option in result.Data) CustomerOptions.Add(new(option.Id, $"{option.Code} · {option.Name}"));
            SelectedCustomers.Clear(); foreach (var option in CustomerOptions.Where(option => retained.Contains(option.Id))) SelectedCustomers.Add(option);
        }
        finally { if (ReferenceEquals(_customerCancellation, cancellation)) _customerCancellation = null; }
    }
    /// <summary>客户候选首次失败后可独立重试，不改变已应用查询或编辑器。</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshCustomerOptionsAsync() => LoadCustomerOptionsAsync();
    private async Task RefreshManagerAsync()
    {
        if (IsSourcePanelOpen && !await RefreshSourceNavigationAsync(reloadCurrentData: false))
        {
            return;
        }

        if (IsSourcePanelOpen && IsSourceSelected && SourceGroups.All(group => group.SourceUid != _appliedSourceUid))
        {
            await LoadPageAsync(_appliedMainQuery ?? CaptureMainQuery(), 1, PageSize);
            return;
        }

        await LoadPageAsync();
    }

    private Task ReloadAfterWriteAsync() => RefreshManagerAsync();

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

    #endregion

    #region 选择状态与辅助方法

    // 本区域维护批量勾选、命令能力、脏状态确认及通用转换。

    private static List<string> SplitLines(string value) => value
        .Split(['\r', '\n', ',', ';', '；', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private async Task<bool> ConfirmDiscardManualChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_closeUnknown || ManualEditor.IsEditorLocked) return false;
        if (!ManualEditor.HasUnsavedChanges)
        {
            return true;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "放弃手工成品需求修改",
            "当前手工成品需求有未保存修改，继续将放弃这些修改。",
            "放弃修改",
            cancellationToken: cancellationToken);
        if (ManualEditor.IsEditorLocked) return false;
        if (confirmed)
        {
            ManualEditor.Close(discardChanges: true);
            ActiveDrawerContent = null;
            IsDrawerOpen = false;
        }

        return confirmed;
    }

    private void OnManualEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GrossDemandManualEditorViewModel.HasUnsavedChanges) or nameof(GrossDemandManualEditorViewModel.IsOpen) or nameof(GrossDemandManualEditorViewModel.HasPendingSave) or nameof(GrossDemandManualEditorViewModel.IsEditorLocked))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            CreateManualCommand.NotifyCanExecuteChanged();
            EditSelectedManualCommand.NotifyCanExecuteChanged();
            CloseSelectedManualCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            SearchCommand.NotifyCanExecuteChanged();
            ClearFiltersCommand.NotifyCanExecuteChanged();
            PageChangeCommand.NotifyCanExecuteChanged();
            SelectSourceGroupCommand.NotifyCanExecuteChanged();
            ToggleSourcePanelCommand.NotifyCanExecuteChanged();
            ClearSourceFiltersCommand.NotifyCanExecuteChanged();
        }
    }

    private async void OnManualSaved(object? sender, EventArgs e)
    {
        await ReloadAfterWriteAsync();
    }

    private bool CanRun() => !_disposed && IsNotBusy && !_closeUnknown && !ManualEditor.IsEditorLocked && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceView);
    /// <summary>手工创建使用独立维护权限，模块只读访问不能开启编辑器。</summary>
    private bool CanCreateManual() => CanWrite && CanRun()
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage);
    private bool CanPageChange() => CanRun() && !IsSourceSelected;
    private bool CanSelectSourceGroup(GrossDemandSourceGroupModel? group) => CanRun() && !IsSourceGroupLoading && group is not null;
    private bool CanUseSelected() => CanRun() && SelectedItem is not null;
    private bool CanOpenTracking() => CanUseSelected() && _currentUser.HasPermission(PermissionKeys.OrderTrackingView);
    private bool CanOpenSource() => CanUseSelected() && SelectedItem is { SourceUid: var id } && id != Guid.Empty && SelectedItem.SourceType switch
    { "SO" => _currentUser.HasPermission(PermissionKeys.SalesOrderView), "CR" => _currentUser.HasPermission(PermissionKeys.QualityControlAfterSalesView), "MD" => CanEditSelectedManual(), _ => false };
    private bool CanEditSelectedManual() => CanWrite && CanRun()
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage)
        && SelectedItem is { SourceType: "MD", LifecycleStatus: GrossDemandLifecycleStatusEnum.Active };
    private bool CanCloseSelectedManual() => CanWrite && IsNotBusy
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage)
        && (!_closeUnknown || _pendingCloseUserId == _currentUser.CurrentUser?.UserId)
        && (_closeUnknown || CanEditSelectedManual());

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _customerCancellation?.Cancel();
        CancelSourceGroupLoading();
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();

        ManualEditor.PropertyChanged -= OnManualEditorPropertyChanged;
        ManualEditor.Saved -= OnManualSaved;
        SourceLifecycleStatuses.CollectionChanged -= OnSourceGroupFilterChanged;
        SelectedCustomers.CollectionChanged -= OnDraftCollectionChanged;
        SelectedSourceTypes.CollectionChanged -= OnDraftCollectionChanged;
        SelectedLifecycleStatuses.CollectionChanged -= OnDraftCollectionChanged;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        ManualEditor.Dispose();
    }
}
