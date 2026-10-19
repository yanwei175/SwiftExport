using Mapster;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExportClient.Features.OrderTracking.Overview.Policies;
using SwiftExportClient.Features.OrderTracking.DemandDetail.Models;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.OrderTracking.Overview.ViewModels;

/// <summary>来源主表服务端分页，选中来源后读取全部非删除成品需求并支持本地列筛选。</summary>
public sealed partial class OrderTrackingOverviewViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>跟踪总览工作区路由。</summary>
    public const string RouteKey = "order-tracking-overview";
    private readonly IOrderTrackingApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _sourceCancellation;
    private CancellationTokenSource? _demandCancellation;
    private CancellationTokenSource? _customerCancellation;
    private bool _sourcesAccepted;
    private OrderTrackingQuerySnapshot _applied = new(string.Empty, "SO", null, null, [], [], []);
    private long _sourceVersion;
    private long _demandVersion;
    private bool _initialized;
    private bool _disposed;

    /// <summary>注入独立跟踪查询、导航及会话，不借用其他模块读取权限。</summary>
    public OrderTrackingOverviewViewModel(IOrderTrackingApi api, IApiCallRunner runner, INavigationService navigation, ICurrentUserService currentUser)
    {
        _api = api; _runner = runner; _navigation = navigation; _currentUser = currentUser;
        Title = DocumentTitle; IsReadOnly = true;
        Summaries = new(api, runner, currentUser);
        Matches = new(api, runner, currentUser);
        UpdateAttentionOptions();
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        SelectedCustomerCodes.CollectionChanged += OnCollectionFilterChanged;
        SelectedLifecycleStatuses.CollectionChanged += OnCollectionFilterChanged;
        SelectedAttentions.CollectionChanged += OnCollectionFilterChanged;
    }

    /// <summary>当前页来源组，身份由来源类型与 SourceGroupId 组成。</summary>
    public ObservableCollection<OrderTrackingSourceRowModel> Sources { get; } = [];
    /// <summary>当前来源的完整非删除成品需求，不沿用主表筛选。</summary>
    public ObservableCollection<OrderTrackingDemandRowModel> Items { get; } = [];
    /// <summary>独立客户候选。</summary>
    public ObservableCollection<string> CustomerCodeOptions { get; } = [];
    /// <summary>已提交的客户代码多选。</summary>
    public ObservableCollection<string> SelectedCustomerCodes { get; } = [];
    /// <summary>已提交的生命周期多选。</summary>
    public ObservableCollection<TrackingFilterOption<GrossDemandLifecycleStatusEnum>> SelectedLifecycleStatuses { get; } = [];
    /// <summary>来源范围，空值表示全部。</summary>
    public IReadOnlyList<TrackingFilterOption<string>> SourceOptions { get; } =
    [new("SO", "销售订单 SO"), new("CR", "投诉补货 CR"), new("MD", "手工需求 MD"), new("", "全部来源")];
    /// <summary>需求生命周期筛选。</summary>
    public IReadOnlyList<TrackingFilterOption<GrossDemandLifecycleStatusEnum>> LifecycleOptions { get; } =
    [new(GrossDemandLifecycleStatusEnum.Active, "活动"), new(GrossDemandLifecycleStatusEnum.Closed, "已关闭"), new(GrossDemandLifecycleStatusEnum.Voided, "已作废")];

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedSourceType { get; set; } = "SO";
    [ObservableProperty] public partial DateTime? RequiredDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? RequiredDateTo { get; set; }
    [ObservableProperty] public partial int SourcePageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int SourcePageSize { get; private set; } = 20;
    [ObservableProperty] public partial int SourceTotalCount { get; private set; }

    [ObservableProperty] public partial bool IsLoadingSources { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SourceSummary))]
    public partial int VisibleSourceCount { get; set; }
    /// <summary>总来源、本页加载和本地列筛选可见数分别显示。</summary>
    public string SourceSummary => $"总来源 {SourceTotalCount} · 本页加载 {Sources.Count} · 当前可见 {VisibleSourceCount}";
    [ObservableProperty] public partial bool HasUnappliedChanges { get; private set; }
    /// <summary>仅在成功应用查询后更新，明确当前结果使用哪一批条件。</summary>
    public string QueryStateText => HasUnappliedChanges ? "条件已修改，尚未查询；当前结果、刷新和分页仍使用已应用条件。" : "当前结果使用已应用条件。";
    [ObservableProperty] public partial bool IsUpdatingSources { get; private set; }
    [ObservableProperty] public partial bool IsUpdatingDemands { get; private set; }
    [ObservableProperty] public partial bool HasLoadedDemands { get; private set; }
    /// <summary>子表标题明确来源、客户及完整响应条数，不使用筛选后的条数。</summary>
    public string DemandTitle => SelectedSource is null ? "全部成品需求" : $"{SelectedSource.SourceNo} · 客户 {SelectedSource.CustomerCode} · 全量 {LoadedDemandCount} 条";
    /// <summary>区分未选择、未加载、加载、读取失败、真实空和本地筛选空。</summary>
    public string DemandStateText => SelectedSource is null ? "请选择来源查看成品需求。"
        : IsLoading ? "正在加载全部成品需求…" : DemandLoadError is not null ? "成品需求读取失败，可单独重试；已有结果保留。"
        : !HasLoadedDemands ? "尚未加载成品需求。" : Items.Count == 0 ? "该来源没有非删除成品需求。"
        : VisibleDemandCount == 0 ? "本地列筛选无结果，可清除列筛选查看全量。" : string.Empty;
    /// <summary>完整响应的条数，不受本地表头筛选影响。</summary>
    public int LoadedDemandCount => Items.Count;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    public partial int VisibleDemandCount { get; set; }
    /// <summary>区分来源全量与本地筛选后的显示条数。</summary>
    public string ResultSummary => SelectedSource is null ? "请选择来源查看全部成品需求。"
        : $"{SelectedSource.SourceNo} · 全来源已加载 {LoadedDemandCount} 条；当前筛选显示 {VisibleDemandCount} 条。";
    /// <summary>关注项候选仅含当前有权查看的维度。</summary>
    public ObservableCollection<TrackingFilterOption<string>> AttentionOptions { get; } = [];
    /// <summary>多选关注码由后端求并集，再与基础条件在同一需求上求交。</summary>
    public ObservableCollection<TrackingFilterOption<string>> SelectedAttentions { get; } = [];
    /// <summary>来源页和完整子表摘要拥有独立请求及重试，不阻塞基础事实。</summary>
    public OrderTrackingOverviewSummaryViewModel Summaries { get; }
    /// <summary>服务端命中标记独立于基础表和摘要，不裁剪完整行集合。</summary>
    public OrderTrackingMatchesViewModel Matches { get; }
    private void UpdateAttentionOptions()
    {
        var options = OrderTrackingAttentionPolicy.GetOptions(_currentUser);
        AttentionOptions.Clear(); foreach (var option in options) AttentionOptions.Add(option);
        foreach (var selected in SelectedAttentions.Where(x => !options.Any(option => option.Value == x.Value)).ToArray()) SelectedAttentions.Remove(selected);
    }    [ObservableProperty] public partial string? DemandLoadError { get; private set; }
    [ObservableProperty] public partial string? CustomerLoadError { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedSourceCommand))]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    public partial OrderTrackingSourceRowModel? SelectedSource { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDemandSectionCommand))]
    public partial OrderTrackingDemandRowModel? SelectedItem { get; set; }

    /// <summary>跟踪独立权限。</summary>
    public bool CanView => !_disposed && _currentUser.HasPermission(PermissionKeys.OrderTrackingView);
    /// <inheritdoc />
    public string DocumentTitle => "跟踪总览";
    /// <inheritdoc />
    public bool HasUnsavedChanges => false;
    /// <inheritdoc />
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    partial void OnKeywordChanged(string value) => MarkDraftChanged();
    partial void OnSelectedSourceTypeChanged(string value) => MarkDraftChanged();
    partial void OnRequiredDateFromChanged(DateTime? value) => MarkDraftChanged();
    partial void OnRequiredDateToChanged(DateTime? value) => MarkDraftChanged();
    partial void OnSelectedSourceChanged(OrderTrackingSourceRowModel? value)
    { if (!IsUpdatingSources) ClearDemands(); NotifyPresentation(); }
    partial void OnSelectedItemChanged(OrderTrackingDemandRowModel? value) => NotifyPresentation();
    partial void OnIsLoadingChanged(bool value) => NotifyPresentation();
    partial void OnDemandLoadErrorChanged(string? value) => NotifyPresentation();
    partial void OnHasLoadedDemandsChanged(bool value) => NotifyPresentation();
    partial void OnVisibleDemandCountChanged(int value) => OnPropertyChanged(nameof(DemandStateText));
    partial void OnSourceTotalCountChanged(int value) => OnPropertyChanged(nameof(SourceSummary));
    private void OnCollectionFilterChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkDraftChanged();
    /// <summary>只记录输入差异，不取消已应用查询的读取，也不清除任何结果。</summary>
    private void MarkDraftChanged()
    { HasUnappliedChanges = !_applied.HasSameConditions(CaptureDraft()); OnPropertyChanged(nameof(QueryStateText)); }
    private OrderTrackingQuerySnapshot CaptureDraft() => new(Keyword.Trim(), SelectedSourceType, RequiredDateFrom?.Date, RequiredDateTo?.Date,
        SelectedCustomerCodes.ToArray(), SelectedLifecycleStatuses.Select(x => x.Value).ToArray(), SelectedAttentions.Select(x => x.Value).ToArray());
    /// <summary>重评当前行操作提示；明细沿用各自权限，不以摘要权限替代。</summary>
    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(DemandTitle)); OnPropertyChanged(nameof(DemandStateText)); OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(DetailHint)); OnPropertyChanged(nameof(DeliveryHint)); OnPropertyChanged(nameof(MaterialHint)); OnPropertyChanged(nameof(PurchaseHint)); OnPropertyChanged(nameof(ReceiptHint));
    }
    private string SectionHint(DemandTrackingSection section) => !CanOpenSelected() ? "请先选择一条成品需求；复制可多选。"
        : CanOpenDemandSection(section) ? "查看当前行的明细证据。" : "当前账号缺少该明细区域的查看权限。";
    /// <summary>当前行基础明细操作提示。</summary>
    public string DetailHint => SectionHint(DemandTrackingSection.Basic);
    /// <summary>交付区域的操作提示。</summary>
    public string DeliveryHint => SectionHint(DemandTrackingSection.Deliveries);
    /// <summary>物料区域的操作提示。</summary>
    public string MaterialHint => SectionHint(DemandTrackingSection.Materials);
    /// <summary>采购区域的操作提示。</summary>
    public string PurchaseHint => SectionHint(DemandTrackingSection.Purchases);
    /// <summary>收货区域的操作提示。</summary>
    public string ReceiptHint => SectionHint(DemandTrackingSection.Receipts);
    /// <summary>清除来源下的旧投影并取消在途读取，避免筛选后显示其他来源的子表。</summary>
    private void ClearDemands()
    {
        ++_demandVersion; _demandCancellation?.Cancel(); Summaries.ResetDemands();
        Matches.Reset(); Items.Clear(); SelectedItem = null; VisibleDemandCount = 0; IsLoading = false; HasLoadedDemands = false;
        OnPropertyChanged(nameof(LoadedDemandCount)); NotifyPresentation();
        DemandLoadError = null;
    }

    /// <summary>首次打开加载来源与候选，重复激活保留两表状态。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || !CanView) return;
        _initialized = true;
        await Task.WhenAll(LoadCustomerCodesAsync(), QuerySourcesAsync(CaptureDraft(), 1, SourcePageSize, false));
    }

    /// <summary>统一关键词及业务筛选从来源第一页查询。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => QuerySourcesAsync(CaptureDraft(), 1, SourcePageSize, false);
    /// <summary>刷新当前来源页和客户候选，保留筛选输入。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => Task.WhenAll(LoadCustomerCodesAsync(), QuerySourcesAsync(_applied, SourcePageIndex, SourcePageSize, true));
    /// <summary>恢复默认来源及业务条件，列筛选由视图先调用现有清除命令。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task ClearFiltersAsync()
    {
        Keyword = string.Empty; SelectedSourceType = "SO"; SelectedAttentions.Clear(); RequiredDateFrom = RequiredDateTo = null;
        SelectedCustomerCodes.Clear(); SelectedLifecycleStatuses.Clear();
        return QuerySourcesAsync(CaptureDraft(), 1, SourcePageSize, false);
    }
    /// <summary>来源主表独立服务端分页。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task SourcePageChangeAsync(PageChangeRequest? request) => request is null ? Task.CompletedTask
        : QuerySourcesAsync(_applied, request.IsPageSizeChanged ? 1 : request.NewIndex, request.NewSize, true);
    private bool CanLoadSource() => CanView && _sourcesAccepted && SelectedSource is not null && Sources.Contains(SelectedSource);
    /// <summary>真实来源选择后加载该来源全部非删除需求。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSource), AllowConcurrentExecutions = true)]
    private Task LoadSelectedSourceAsync() => QueryDemandsAsync();
    private bool CanOpenSelected() => CanLoadSource() && SelectedItem is { GrossDemandId: var id } && id != Guid.Empty && Items.Contains(SelectedItem);
    /// <summary>按稳定需求身份复用基础和下游跟踪明细页签。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private Task OpenSelectedAsync() => CanOpenSelected()
        ? _navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, SelectedItem!.GrossDemandId.ToString("N"), SelectedItem.GrossDemandId)
        : Task.CompletedTask;

    private bool CanOpenDemandSection(DemandTrackingSection section) => CanOpenSelected() && section switch
    {
        DemandTrackingSection.Materials => _currentUser.HasPermission(PermissionKeys.DemandPlanningMaterialView),
        DemandTrackingSection.Purchases => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead),
        DemandTrackingSection.Receipts => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead) && _currentUser.HasPermission(PermissionKeys.InventoryInboundView),
        DemandTrackingSection.Deliveries => _currentUser.HasPermission(PermissionKeys.ShippingPackingListView),
        _ => true
    };
    /// <summary>摘要失败仍可按明细原权限查看证据；复用文档时明确激活目标区域。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDemandSection))]
    private async Task OpenDemandSectionAsync(DemandTrackingSection section)
    {
        if (!CanOpenDemandSection(section)) return;
        var id = SelectedItem!.GrossDemandId; var user = _currentUser.CurrentUser;
        await _navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, id.ToString("N"), new DemandTrackingOpenRequest(id, section));
        if (!CanView || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
        var tab = _navigation.OpenTabs.FirstOrDefault(x => x.RouteKey == DemandTrackingDetailViewModel.RouteKey && x.DocumentKey == id.ToString("N"));
        if (tab?.ContentViewModel is DemandTrackingDetailViewModel detail) await detail.ActivateSectionAsync(section);
    }

    /// <summary>来源查询成功后原子应用冻结条件；失败保留上一批事实与条件。</summary>
    private async Task QuerySourcesAsync(OrderTrackingQuerySnapshot query, int page, int size, bool restore)
    {
        if (!CanView) return;
        if (query.RequiredDateFrom > query.RequiredDateTo) { ValidationSummary = "要求交期开始日期不能晚于结束日期。"; return; }
        if (query.AttentionCodes.Any(code => !AttentionOptions.Any(option => option.Value == code)))
        { ValidationSummary = "关注项未知或缺少所需维度权限。"; return; }
        var version = ++_sourceVersion; _sourceCancellation?.Cancel();
        ++_demandVersion; _demandCancellation?.Cancel(); IsLoading = false; Matches.CancelPending();
        using var cancellation = new CancellationTokenSource(); _sourceCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser; var request = query.ForPage(page, size);
        IsLoadingSources = true; ValidationSummary = null;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchSourcesAsync(request, token), token, "来源查询失败");
            if (token.IsCancellationRequested || version != _sourceVersion || !CanView || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            if (!result.Success || result.Data is null) { ValidationSummary = result.Message; return; }
            if (result.Data.Items.GroupBy(x => (x.SourceType, x.SourceGroupId)).Any(x => x.Count() > 1))
            { ValidationSummary = "来源响应身份重复，请重试。"; return; }
            var key = restore && SelectedSource is not null ? (SelectedSource.SourceType, SelectedSource.SourceGroupId) : default;
            _applied = query;
            IsUpdatingSources = true;
            try
            {
                var existingRows = Sources.ToDictionary(row => (row.SourceType, row.SourceGroupId));
                var rows = result.Data.Items.Select(dto =>
                {
                    var existing = existingRows.GetValueOrDefault((dto.SourceType, dto.SourceGroupId));
                    return existing is null ? dto.Adapt<OrderTrackingSourceRowModel>() : dto.Adapt(existing);
                }).ToArray();
                Reconcile(Sources, rows);
                SelectedSource = restore ? Sources.FirstOrDefault(row => row.SourceType == key.Item1 && row.SourceGroupId == key.Item2) : null;
                _sourcesAccepted = true; SourcePageIndex = request.PageIndex; SourcePageSize = request.PageSize; SourceTotalCount = result.Data.TotalCount;
                VisibleSourceCount = Sources.Count; OnPropertyChanged(nameof(SourceSummary));
                Summaries.BindSources(Sources.ToArray());
            }
            finally { IsUpdatingSources = false; }
            MarkDraftChanged(); LoadSelectedSourceCommand.NotifyCanExecuteChanged();
            if (SelectedSource is null) ClearDemands(); else await QueryDemandsAsync();
        }
        finally
        {
            if (version == _sourceVersion) IsLoadingSources = false;
            if (ReferenceEquals(_sourceCancellation, cancellation)) _sourceCancellation = null;
        }
    }
    /// <summary>保留稳定行对象与集合实例，减少刷新引起的选择、滚动和本地列筛选丢失。</summary>
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> rows)
    {
        var accepted = rows.ToHashSet();
        foreach (var old in target.Where(row => !accepted.Contains(row)).ToArray()) target.Remove(old);
        for (var index = 0; index < rows.Count; index++)
        {
            if (index < target.Count && EqualityComparer<T>.Default.Equals(target[index], rows[index])) continue;
            var current = target.IndexOf(rows[index]);
            if (current < 0) target.Insert(index, rows[index]); else if (current != index) target.Move(current, index);
        }
    }
    /// <summary>只按稳定来源身份读取完整非删除需求，不复制主表筛选，也不截断响应。</summary>
    private async Task QueryDemandsAsync()
    {
        if (!CanLoadSource()) return;
        var source = SelectedSource!;
        var version = ++_demandVersion; _demandCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _demandCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser;
        Matches.CancelPending(); IsLoading = true; DemandLoadError = null;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetSourceDemandsAsync(source.SourceType, source.SourceGroupId, token), token, "来源全部成品需求读取失败");
            if (token.IsCancellationRequested || version != _demandVersion || !CanLoadSource()
                || !ReferenceEquals(source, SelectedSource) || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            if (!result.Success || result.Data is null) { DemandLoadError = result.Message; return; }
            if (result.Data.SourceGroupId != source.SourceGroupId || !string.Equals(result.Data.SourceType, source.SourceType, StringComparison.Ordinal))
            { DemandLoadError = "返回的来源身份不一致，请重新查询。"; return; }
            var selectedId = SelectedItem?.GrossDemandId;
            if (result.Data.Items.GroupBy(x => x.GrossDemandId).Any(x => x.Key == Guid.Empty || x.Count() > 1))
            { DemandLoadError = "成品需求响应身份重复或缺失，请重试。"; return; }
            IsUpdatingDemands = true;
            try
            {
                var existingRows = Items.ToDictionary(row => row.GrossDemandId);
                var rows = result.Data.Items.Select(dto =>
                {
                    var existing = existingRows.GetValueOrDefault(dto.GrossDemandId);
                    return existing is null ? dto.Adapt<OrderTrackingDemandRowModel>() : dto.Adapt(existing);
                }).ToArray();
                Reconcile(Items, rows);
            }
            finally { IsUpdatingDemands = false; }
            HasLoadedDemands = true;
            SelectedItem = Items.FirstOrDefault(x => x.GrossDemandId == selectedId);
            VisibleDemandCount = Items.Count;
            OnPropertyChanged(nameof(LoadedDemandCount)); NotifyPresentation();
            Summaries.BindDemands(source.SourceType, source.SourceGroupId, Items.ToArray());
            Matches.Bind(source.SourceType, source.SourceGroupId, Items.ToArray(), _applied, source.MatchedDemandCount);
            OpenSelectedCommand.NotifyCanExecuteChanged(); OpenDemandSectionCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            if (version == _demandVersion) IsLoading = false;
            if (ReferenceEquals(_demandCancellation, cancellation)) _demandCancellation = null;
        }
    }
    /// <summary>客户候选使用独立请求和权限，失败不遮挡来源表。</summary>
    private async Task LoadCustomerCodesAsync()
    {
        if (!CanView) return;
        _customerCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _customerCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetCustomerCodesAsync(token), token, "客户候选加载失败");
            if (token.IsCancellationRequested || !CanView || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            CustomerLoadError = result.Success ? null : result.Message;
            if (!result.Success || result.Data is null) return;
            CustomerCodeOptions.Clear(); foreach (var code in result.Data) CustomerCodeOptions.Add(code);
        }
        finally { if (ReferenceEquals(_customerCancellation, cancellation)) _customerCancellation = null; }
    }

    /// <summary>会话替换清除两表与候选，旧响应不能跨会话接纳。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        ++_sourceVersion; _sourceCancellation?.Cancel(); _sourcesAccepted = false; Summaries.ResetSources(); Sources.Clear(); SelectedSource = null; ClearDemands(); SourcePageIndex = 1; SourceTotalCount = 0; VisibleSourceCount = 0; IsLoadingSources = false; _applied = new(string.Empty, "SO", null, null, [], [], []); _customerCancellation?.Cancel(); CustomerCodeOptions.Clear(); SelectedCustomerCodes.Clear();
        UpdateAttentionOptions(); MarkDraftChanged();
        ValidationSummary = CustomerLoadError = null; OnPropertyChanged(nameof(CanView));
        SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); ClearFiltersCommand.NotifyCanExecuteChanged();
        SourcePageChangeCommand.NotifyCanExecuteChanged(); LoadSelectedSourceCommand.NotifyCanExecuteChanged();
        OpenSelectedCommand.NotifyCanExecuteChanged(); OpenDemandSectionCommand.NotifyCanExecuteChanged();
    }
    /// <summary>释放会话和集合订阅，取消本页拥有的全部读取。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        ++_sourceVersion; ++_demandVersion;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        SelectedCustomerCodes.CollectionChanged -= OnCollectionFilterChanged;
        SelectedLifecycleStatuses.CollectionChanged -= OnCollectionFilterChanged;
        SelectedAttentions.CollectionChanged -= OnCollectionFilterChanged;
        Summaries.Dispose(); Matches.Dispose();
        _sourceCancellation?.Cancel(); _demandCancellation?.Cancel(); _customerCancellation?.Cancel();
    }
}
