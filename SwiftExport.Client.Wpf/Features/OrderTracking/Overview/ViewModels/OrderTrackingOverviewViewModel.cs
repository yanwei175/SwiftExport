using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
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

/// <summary>来源主表与命中成品需求子表分别服务端分页；子表严格使用已接纳的来源查询条件。</summary>
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
    private OrderTrackingSourceSearchRequest? _acceptedFilters;
    private long _sourceVersion;
    private long _demandVersion;
    private bool _initialized;
    private bool _disposed;

    /// <summary>注入独立跟踪查询、导航及会话，不借用其他模块读取权限。</summary>
    public OrderTrackingOverviewViewModel(IOrderTrackingApi api, IApiCallRunner runner, INavigationService navigation, ICurrentUserService currentUser)
    {
        _api = api; _runner = runner; _navigation = navigation; _currentUser = currentUser;
        Title = DocumentTitle; IsReadOnly = true;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        SelectedCustomerCodes.CollectionChanged += OnCollectionFilterChanged;
        SelectedLifecycleStatuses.CollectionChanged += OnCollectionFilterChanged;
    }

    /// <summary>当前页来源组，身份由来源类型与 SourceGroupId 组成。</summary>
    public ObservableCollection<OrderTrackingSourceGroupDto> Sources { get; } = [];
    /// <summary>当前来源下符合已接纳条件的一页成品需求。</summary>
    public ObservableCollection<OrderTrackingListItemDto> Items { get; } = [];
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
    [ObservableProperty] public partial int PageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int PageSize { get; private set; } = 20;
    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty] public partial bool IsLoadingSources { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string SourceSummary { get; private set; } = "尚未查询来源。";
    [ObservableProperty] public partial string ResultSummary { get; private set; } = "请选择来源查看命中成品需求。";
    [ObservableProperty] public partial string? DemandLoadError { get; private set; }
    [ObservableProperty] public partial string? CustomerLoadError { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(PageChangeCommand))]
    public partial OrderTrackingSourceGroupDto? SelectedSource { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    public partial OrderTrackingListItemDto? SelectedItem { get; set; }

    /// <summary>跟踪独立权限。</summary>
    public bool CanView => !_disposed && _currentUser.HasPermission(PermissionKeys.OrderTrackingView);
    /// <inheritdoc />
    public string DocumentTitle => "跟踪总览";
    /// <inheritdoc />
    public bool HasUnsavedChanges => false;
    /// <inheritdoc />
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    partial void OnKeywordChanged(string value) => InvalidateFilters();
    partial void OnSelectedSourceTypeChanged(string value) => InvalidateFilters();
    partial void OnRequiredDateFromChanged(DateTime? value) => InvalidateFilters();
    partial void OnRequiredDateToChanged(DateTime? value) => InvalidateFilters();
    partial void OnSelectedSourceChanged(OrderTrackingSourceGroupDto? value) => ClearDemands();
    private void OnCollectionFilterChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateFilters();

    /// <summary>筛选修改立即失效旧查询与来源选择，输入过程不自动发请求。</summary>
    private void InvalidateFilters()
    {
        ++_sourceVersion; _sourceCancellation?.Cancel(); _acceptedFilters = null;
        Sources.Clear(); SelectedSource = null; ClearDemands();
        SourcePageIndex = 1; SourceTotalCount = 0; IsLoadingSources = false;
        ValidationSummary = null; SourceSummary = "条件已修改，请查询来源。";
    }

    /// <summary>清除来源下的旧投影并取消在途读取，避免筛选后显示其他来源的子表。</summary>
    private void ClearDemands()
    {
        ++_demandVersion; _demandCancellation?.Cancel();
        Items.Clear(); SelectedItem = null; PageIndex = 1; TotalCount = 0; IsLoading = false;
        DemandLoadError = null; ResultSummary = "请选择来源查看命中成品需求。";
    }

    /// <summary>首次打开加载来源与候选，重复激活保留两表状态。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || !CanView) return;
        _initialized = true;
        await Task.WhenAll(LoadCustomerCodesAsync(), QuerySourcesAsync(1, SourcePageSize));
    }

    /// <summary>统一关键词及业务筛选从来源第一页查询。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => QuerySourcesAsync(1, SourcePageSize);
    /// <summary>刷新当前来源页和客户候选，保留筛选输入。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => Task.WhenAll(LoadCustomerCodesAsync(), QuerySourcesAsync(SourcePageIndex, SourcePageSize));
    /// <summary>恢复默认来源及业务条件，列筛选由视图先调用现有清除命令。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task ClearFiltersAsync()
    {
        Keyword = string.Empty; SelectedSourceType = "SO"; RequiredDateFrom = RequiredDateTo = null;
        SelectedCustomerCodes.Clear(); SelectedLifecycleStatuses.Clear();
        return QuerySourcesAsync(1, SourcePageSize);
    }
    /// <summary>来源主表独立服务端分页。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task SourcePageChangeAsync(PageChangeRequest? request) => request is null ? Task.CompletedTask
        : QuerySourcesAsync(request.IsPageSizeChanged ? 1 : request.NewIndex, request.NewSize);
    private bool CanLoadSource() => CanView && _acceptedFilters is not null && SelectedSource is not null && Sources.Contains(SelectedSource);
    /// <summary>真实来源选择后加载命中需求第一页。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSource), AllowConcurrentExecutions = true)]
    private Task LoadSelectedSourceAsync() => QueryDemandsAsync(1, PageSize);
    /// <summary>子表独立分页，不改变来源主表页码。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSource), AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request) => request is null ? Task.CompletedTask
        : QueryDemandsAsync(request.IsPageSizeChanged ? 1 : request.NewIndex, request.NewSize);
    private bool CanOpenSelected() => CanLoadSource() && SelectedItem is { GrossDemandId: var id } && id != Guid.Empty && Items.Contains(SelectedItem);
    /// <summary>按稳定需求身份复用基础和下游跟踪明细页签。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private Task OpenSelectedAsync() => CanOpenSelected()
        ? _navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, SelectedItem!.GrossDemandId.ToString("N"), SelectedItem.GrossDemandId)
        : Task.CompletedTask;

    /// <summary>冻结筛选后查询来源；新条件、会话或版本失效时拒绝响应。</summary>
    private async Task QuerySourcesAsync(int page, int size)
    {
        if (!CanView) return;
        var version = ++_sourceVersion; _sourceCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _sourceCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser;
        SelectedSource = null; ClearDemands(); _acceptedFilters = null;
        if (RequiredDateFrom?.Date > RequiredDateTo?.Date)
        { IsLoadingSources = false; ValidationSummary = "要求交期开始日期不能晚于结束日期。"; _sourceCancellation = null; return; }
        var request = new OrderTrackingSourceSearchRequest
        {
            Keyword = Keyword.Trim(), PageIndex = Math.Max(1, page), PageSize = Math.Clamp(size, 1, 200),
            SourceTypes = string.IsNullOrEmpty(SelectedSourceType) ? [] : [SelectedSourceType],
            CustomerCodes = SelectedCustomerCodes.ToList(), RequiredDateFrom = RequiredDateFrom?.Date, RequiredDateTo = RequiredDateTo?.Date,
            LifecycleStatuses = SelectedLifecycleStatuses.Select(x => x.Value).ToList()
        };
        IsLoadingSources = true; ValidationSummary = null;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchSourcesAsync(request, token), token, "来源查询失败");
            if (token.IsCancellationRequested || version != _sourceVersion || !CanView || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            if (!result.Success || result.Data is null) { ValidationSummary = result.Message; return; }
            Sources.Clear(); foreach (var source in result.Data.Items) Sources.Add(source);
            _acceptedFilters = request;
            SourcePageIndex = request.PageIndex; SourcePageSize = request.PageSize; SourceTotalCount = result.Data.TotalCount;
            SourceSummary = $"共 {SourceTotalCount} 个命中来源，当前加载 {Sources.Count} 个。";
            LoadSelectedSourceCommand.NotifyCanExecuteChanged(); PageChangeCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            if (version == _sourceVersion) IsLoadingSources = false;
            if (ReferenceEquals(_sourceCancellation, cancellation)) _sourceCancellation = null;
        }
    }

    /// <summary>子表复制已成功来源查询的条件，只额外限定稳定来源组身份。</summary>
    private async Task QueryDemandsAsync(int page, int size)
    {
        if (!CanLoadSource()) return;
        var source = SelectedSource!; var filters = _acceptedFilters!;
        var version = ++_demandVersion; _demandCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _demandCancellation = cancellation;
        var token = cancellation.Token; var user = _currentUser.CurrentUser;
        var request = new OrderTrackingSearchRequest
        {
            Keyword = filters.Keyword, PageIndex = Math.Max(1, page), PageSize = Math.Clamp(size, 1, 200),
            SourceTypes = filters.SourceTypes.ToList(), CustomerCodes = filters.CustomerCodes.ToList(),
            RequiredDateFrom = filters.RequiredDateFrom, RequiredDateTo = filters.RequiredDateTo,
            LifecycleStatuses = filters.LifecycleStatuses.ToList(), SourceGroupType = source.SourceType, SourceGroupId = source.SourceGroupId
        };
        IsLoading = true; DemandLoadError = null;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(request, token), token, "命中成品需求查询失败");
            if (token.IsCancellationRequested || version != _demandVersion || !CanLoadSource()
                || !ReferenceEquals(source, SelectedSource) || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            if (!result.Success || result.Data is null) { DemandLoadError = result.Message; return; }
            var selectedId = SelectedItem?.GrossDemandId;
            Items.Clear(); foreach (var item in result.Data.Items) Items.Add(item);
            SelectedItem = Items.FirstOrDefault(x => x.GrossDemandId == selectedId);
            PageIndex = request.PageIndex; PageSize = request.PageSize; TotalCount = result.Data.TotalCount;
            ResultSummary = $"{source.SourceNo} · 全部 {source.TotalDemandCount} 条 / 条件命中 {source.MatchedDemandCount} 条；当前加载 {Items.Count} 条。";
            OpenSelectedCommand.NotifyCanExecuteChanged();
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
        InvalidateFilters(); _customerCancellation?.Cancel(); CustomerCodeOptions.Clear(); SelectedCustomerCodes.Clear();
        ValidationSummary = CustomerLoadError = null; OnPropertyChanged(nameof(CanView));
        SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); ClearFiltersCommand.NotifyCanExecuteChanged();
        SourcePageChangeCommand.NotifyCanExecuteChanged(); LoadSelectedSourceCommand.NotifyCanExecuteChanged();
        PageChangeCommand.NotifyCanExecuteChanged(); OpenSelectedCommand.NotifyCanExecuteChanged();
    }
    /// <summary>释放会话和集合订阅，取消本页拥有的全部读取。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        ++_sourceVersion; ++_demandVersion;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        SelectedCustomerCodes.CollectionChanged -= OnCollectionFilterChanged;
        SelectedLifecycleStatuses.CollectionChanged -= OnCollectionFilterChanged;
        _sourceCancellation?.Cancel(); _demandCancellation?.Cancel(); _customerCancellation?.Cancel();
    }
}
