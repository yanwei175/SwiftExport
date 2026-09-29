using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.SysEnums;
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

/// <summary>管理来源需求本体查询、来源导航及当前兼容的手工需求与拆解命令。</summary>
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
    private bool _initialized;
    private bool _syncingChecks;
    private bool _syncingSourceFilters;
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

        SourceLifecycleStatuses.Add(LifecycleStatusOptions[0]);
        SourceLifecycleStatuses.CollectionChanged += OnSourceGroupFilterChanged;
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

    /// <summary>当前链路节点筛选；“已交付”仅表示有有效正式交付行。</summary>
    public IReadOnlyList<GrossDemandFilterOption<string>> TraceViewOptions { get; } =
    [
        new("All", "全部链路"),
        new("NoMaterial", "无活动拆解"),
        new("HasMaterial", "有活动拆解"),
        new("NoPurchase", "无可核实采购"),
        new("HasPurchase", "有可核实采购"),
        new("NoDelivery", "无正式交付"),
        new("HasDelivery", "有正式交付"),
    ];

    public ObservableCollection<GrossDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> SelectedLifecycleStatuses { get; } =
        [
        new(GrossDemandLifecycleStatusEnum.Active, "活动")

        ];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> SourceLifecycleStatuses { get; } = [];

    public string DocumentTitle => "毛需求";
    public bool HasUnsavedChanges => ManualEditor.HasUnsavedChanges || ManualEditor.HasPendingSave || _closeUnknown;
    /// <summary>关闭结果未知时保留原来源、版本、原因和命令身份。</summary>
    public bool HasPendingClose => _closeUnknown;
    public int TotalPages => IsSourceSelected || TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => IsSourceSelected ? $"当前来源共 {TotalCount} 条" : $"第 {PageIndex} / {TotalPages} 页";
    public int CheckedCount => Items.Count(item => item.IsChecked);
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
    public partial string CustomerKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GrossDemandFilterOption<string>? SelectedTraceView { get; set; }

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
    public partial string ResultSummary { get; set; } = "尚未加载毛需求。";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SelectedStandardType = StandardTypeOptions[0];
        SelectedTraceView = TraceViewOptions[0];
        await LoadPageAsync();
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

        ExitSourceMode();
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (await ConfirmDiscardManualChangesAsync())
        {
            await RefreshManagerAsync();
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
        CustomerKeyword = string.Empty;
        SelectedTraceView = TraceViewOptions[0];
        SourceNosText = string.Empty;
        SkusText = string.Empty;
        CustomerSkusText = string.Empty;
        RequiredDateFrom = null;
        RequiredDateTo = null;
        SelectedStandardType = StandardTypeOptions[0];
        SelectedSourceTypes.Clear();
        SelectedLifecycleStatuses.Clear();
        ExitSourceMode();
        PageIndex = 1;
        await LoadPageAsync();
    }

    #endregion

    #region 毛需求业务命令

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
                "加载手工毛需求失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cancellation)
                || ManualEditor.IsEditorLocked)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("无法编辑手工毛需求", result.Message);
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
        if (!_closeUnknown && (selected is null
            || !await ConfirmDiscardManualChangesAsync()
            || !await _dialogService.ConfirmAsync(
                "关闭手工毛需求",
                $"关闭 {selected.SourceNo} 后将保留历史且不能继续拆解。是否继续？",
                "关闭")))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            if (!_closeUnknown)
            {
                _pendingClose = new CloseGrossDemandCommand
                {
                    CommandId = Guid.NewGuid(), Uid = selected!.Uid, Version = selected.Version,
                    Reason = "用户从毛需求工作台关闭手工需求"
                };
            }
            var result = await _apiCallRunner.RunAsync(
                () => _api.CloseManualAsync(_pendingClose!.Uid, _pendingClose),
                "关闭手工毛需求失败");
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
            _messageService.ShowSuccess("手工毛需求已关闭", selected?.SourceNo ?? result.Data?.SourceNo ?? string.Empty);
            await ReloadAfterWriteAsync();
        }, "正在关闭手工毛需求...");
    }

    #endregion

    #region 抽屉与来源追溯

    /// <summary>用来源需求 Uid 打开独立只读追踪，页面重新加载当前下游证据。</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task OpenTraceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync()) return;
        await _navigationService.OpenDocumentAsync(
            GrossDemandTraceViewModel.RouteKey,
            $"gross-demand-trace:{selected.Uid:N}",
            new GrossDemandTraceOpenRequest(selected.Uid));
    }

    // 来源单据、拆解结果和手工编辑器共用现有抽屉及脏状态保护。

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
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

        if (string.Equals(selected.SourceType, "MD", StringComparison.OrdinalIgnoreCase))
        {
            if (CanEditSelectedManual())
            {
                await EditSelectedManualAsync();
            }
            else
            {
                _messageService.ShowInfo("来源导航", "该手工来源当前不可编辑，请在来源需求列表查看保留的来源快照。");
            }

            return;
        }

        _messageService.ShowInfo("来源导航", "该来源类型尚未接入独立单据页面，请在来源需求列表查看保留的来源快照。");
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

        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        await LoadPageAsync();
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

        _appliedSourceUid = group.SourceUid;
        SelectedSourceGroup = group;
        PageIndex = 1;
        await LoadPageAsync();
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
        if (_closeUnknown || ManualEditor.HasPendingSave) return;
        if (IsSourcePanelOpen)
        {
            await RefreshSourceNavigationAsync(reloadCurrentData: IsSourceSelected);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearSourceFiltersAsync()
    {
        if (_closeUnknown || ManualEditor.HasPendingSave) return;
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

    private async Task LoadPageAsync()
    {
        if (_closeUnknown || ManualEditor.HasPendingSave) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var selectedUid = SelectedItem?.Uid;

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.SearchAsync(CreateSearchRequest(), cancellation.Token),
                    cancellation.Token,
                    "查询毛需求失败");
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
                    var row = GrossDemandRowModel.FromDto(dto);
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
                    ? $"当前来源共 {TotalCount} 条毛需求，已全部加载。"
                    : $"共 {TotalCount} 条毛需求，本页 {Items.Count} 条。";
            }, "正在查询毛需求...", skipIfBusy: false);
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

    private GrossDemandSearchRequest CreateSearchRequest()
        => IsSourceSelected ? CreateSelectedSourceSearchRequest() : CreateMainSearchRequest();

    private GrossDemandSearchRequest CreateMainSearchRequest() => new()
    {
        Keyword = Keyword.Trim(),
        CustomerKeyword = CustomerKeyword.Trim(),
        TraceView = SelectedTraceView?.Value ?? "All",
        PageIndex = PageIndex,
        PageSize = PageSize,
        SourceTypes = SelectedSourceTypes.Select(item => item.Value).ToList(),
        SourceNos = SplitLines(SourceNosText),
        Skus = SplitLines(SkusText),
        CustomerSkus = SplitLines(CustomerSkusText),
        LifecycleStatuses = SelectedLifecycleStatuses.Select(item => item.Value).ToList(),
        RequiredDateFrom = RequiredDateFrom,
        RequiredDateTo = RequiredDateTo,
        IsNonStandard = SelectedStandardType?.Value,
    };

    private GrossDemandSearchRequest CreateSelectedSourceSearchRequest() => new()
    {
        PageIndex = 1,
        PageSize = PageSize,
        LifecycleStatuses = SourceLifecycleStatuses.Select(item => item.Value).ToList(),
        SourceUid = _appliedSourceUid,
    };

    private GrossDemandSourceGroupSearchRequest CreateSourceGroupSearchRequest() => new()
    {
        LifecycleStatuses = SourceLifecycleStatuses.Select(item => item.Value).ToList(),
    };

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
                "查询毛需求来源分组失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_sourceGroupCancellation, cancellation))
            {
                return false;
            }

            if (!result.Success || result.Data is null)
            {
                SourceGroupSummary = result.Message;
                return false;
            }

            var groups = result.Data.Select(GrossDemandSourceGroupModel.FromDto).ToArray();
            SourceGroups.Clear();
            foreach (var group in groups)
            {
                SourceGroups.Add(group);
            }

            SourceGroupSummary = $"共 {SourceGroups.Count} 张来源单，{result.Data.Sum(item => item.DemandCount)} 条毛需求。";
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

    private Task ReloadAfterWriteAsync() => RefreshManagerAsync();

    private void ExitSourceMode()
    {
        _appliedSourceUid = null;
        SelectedSourceGroup = null;
        PageIndex = 1;
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
            "放弃手工毛需求修改",
            "当前手工毛需求有未保存修改，继续将放弃这些修改。",
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
        if (e.PropertyName != nameof(GrossDemandRowModel.IsChecked) || _syncingChecks)
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

    private bool CanRun() => IsNotBusy && !_closeUnknown && !ManualEditor.IsEditorLocked;
    /// <summary>手工创建使用独立维护权限，模块只读访问不能开启编辑器。</summary>
    private bool CanCreateManual() => CanWrite && CanRun()
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage);
    private bool CanPageChange() => CanRun() && !IsSourceSelected;
    private bool CanSelectSourceGroup(GrossDemandSourceGroupModel? group) => CanRun() && !IsSourceGroupLoading && group is not null;
    private bool CanUseSelected() => IsNotBusy && SelectedItem is not null;
    private bool CanEditSelectedManual() => CanWrite && CanRun()
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage)
        && SelectedItem is { SourceType: "MD", LifecycleStatus: GrossDemandLifecycleStatusEnum.Active };
    private bool CanCloseSelectedManual() => CanWrite && IsNotBusy
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage)
        && (_closeUnknown || CanEditSelectedManual());

    #endregion

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
        CancelSourceGroupLoading();
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        DetachRows();
        ManualEditor.PropertyChanged -= OnManualEditorPropertyChanged;
        ManualEditor.Saved -= OnManualSaved;
        SourceLifecycleStatuses.CollectionChanged -= OnSourceGroupFilterChanged;
        ManualEditor.Dispose();
    }
}
