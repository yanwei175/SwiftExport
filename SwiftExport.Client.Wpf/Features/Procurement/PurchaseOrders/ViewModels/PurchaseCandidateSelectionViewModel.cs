using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>负责按来源完整加载待采购物料，并在内存中筛选和维护当前选择。</summary>
public sealed partial class PurchaseCandidateSelectionViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseDraftApi _api;
    private readonly IApiCallRunner _runner;
    private readonly PurchasePreparationService? _preparationService;
    private CancellationTokenSource? _lifetimeCancellation;
    private CancellationTokenSource? _groupCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _supplierSearchCancellation;
    private bool _disposed;

    /// <summary>初始化采购候选选择器依赖和本地集合视图。</summary>
    public PurchaseCandidateSelectionViewModel(
        IPurchaseDraftApi api,
        PurchasePreparationService preparationService,
        IApiCallRunner runner)
    {
        _api = api;
        _preparationService = preparationService;
        _runner = runner;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = MatchesLocalFilter;
    }

    /// <summary>保留仅验证候选加载的测试入口；供应商搜索需使用完整应用依赖。</summary>
    public PurchaseCandidateSelectionViewModel(IPurchaseDraftApi api, IApiCallRunner runner)
    {
        _api = api;
        _runner = runner;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = MatchesLocalFilter;
    }

    /// <summary>全部入口及仍有采购候选的来源单。</summary>
    public ObservableCollection<PurchaseCandidateSourceGroupModel> SourceGroups { get; } = [];

    /// <summary>当前来源入口一次完整加载的采购候选。</summary>
    public ObservableCollection<PurchaseCandidateItemModel> Items { get; } = [];

    /// <summary>只对当前已加载集合应用筛选的 WPF 视图。</summary>
    public ICollectionView ItemsView { get; }

    public ObservableCollection<ProcurementSupplierCandidateDto> SupplierCandidates { get; } = [];

    public event EventHandler? InputChanged;

    /// <summary>返回已加载集合中全部勾选项；本地筛选不会丢失隐藏行选择。</summary>
    public IReadOnlyList<PurchaseCandidateItemModel> SelectedRows =>
        Items.Where(static item => item.IsChecked).ToArray();

    /// <summary>当前本地筛选后的可见候选数量。</summary>
    public int FilteredCount => ItemsView.Cast<object>().Count();

    /// <summary>当前已勾选候选数量，包含因本地筛选暂时隐藏的行。</summary>
    public int SelectedCount => Items.Count(static item => item.IsChecked);

    /// <summary>当前可见行是否全部勾选。</summary>
    public bool IsAllVisibleChecked
    {
        get
        {
            var visible = GetVisibleRows();
            return visible.Count > 0 && visible.All(static item => item.IsChecked);
        }
    }

    [ObservableProperty]
    public partial PurchaseCandidateSourceGroupModel? SelectedSourceGroup { get; private set; }

    [ObservableProperty]
    public partial PurchaseCandidateSourceGroupModel? PendingSourceGroup { get; private set; }

    /// <summary>是否正在等待用户在当前页面确认来源切换。</summary>
    public bool IsSourceSwitchPending => PendingSourceGroup is not null;

    /// <summary>来源切换确认提示；使用行内状态避免在统一采购弹窗内再打开嵌套 Dialog。</summary>
    public string SourceSwitchPrompt => PendingSourceGroup is null
        ? string.Empty
        : $"切换到 {PendingSourceGroup.DisplaySourceNo} 将清除当前已选的 {SelectedCount} 条物料。";

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOnlyErrors { get; set; }

    [ObservableProperty]
    public partial PurchaseCandidateItemModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsSupplierSearchBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsDetailLoaded { get; private set; }

    [ObservableProperty]
    public partial string SourceGroupStatusText { get; private set; } = "正在加载待采购来源...";

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "请选择左侧来源查看待采购物料。";

    /// <summary>打开时只加载完整来源分组，右侧候选保持为空。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingRequests();
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ClearItems();
        SourceGroups.Clear();
        SelectedSourceGroup = null;
        PendingSourceGroup = null;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
        IsDetailLoaded = false;
        SourceGroupStatusText = "正在加载待采购来源...";
        StatusText = "请选择左侧来源查看待采购物料。";
        await LoadGroupsAsync(_lifetimeCancellation.Token);
    }

    /// <summary>切换来源前确认清除已有选择，再以 latest-request-wins 加载完整候选。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectSourceGroupAsync(PurchaseCandidateSourceGroupModel? group)
    {
        if (group is null || _disposed
            || ReferenceEquals(SelectedSourceGroup, group)
            || _lifetimeCancellation is null
            || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (SelectedCount > 0)
        {
            PendingSourceGroup = group;
            OnPropertyChanged(nameof(IsSourceSwitchPending));
            OnPropertyChanged(nameof(SourceSwitchPrompt));
            return;
        }

        await LoadCandidatesAsync(group, _lifetimeCancellation.Token);
    }

    /// <summary>在当前页面确认清除选择并切换来源。</summary>
    [RelayCommand]
    private async Task ConfirmSourceSwitchAsync()
    {
        var group = PendingSourceGroup;
        PendingSourceGroup = null;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
        if (group is not null && _lifetimeCancellation is { IsCancellationRequested: false } lifetime)
            await LoadCandidatesAsync(group, lifetime.Token);
    }

    /// <summary>取消来源切换并保留当前选择。</summary>
    [RelayCommand]
    private void CancelSourceSwitch()
    {
        PendingSourceGroup = null;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
    }

    /// <summary>显式刷新本地筛选；不会发送后端请求。</summary>
    [RelayCommand]
    private void ApplyFilter() => RefreshLocalFilter();

    /// <summary>清空本地筛选并保留当前加载集合和勾选。</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Keyword = string.Empty;
        SourceNo = string.Empty;
        ShowOnlyErrors = false;
        RefreshLocalFilter();
    }

    /// <summary>仅切换当前筛选可见行，隐藏行的勾选保持不变。</summary>
    [RelayCommand]
    private void ToggleAllVisible()
    {
        if (!IsDetailLoaded || IsLoading) return;
        var visible = GetVisibleRows();
        if (visible.Count == 0) return;
        var target = !visible.All(static item => item.IsChecked);
        foreach (var item in visible) item.IsChecked = target;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        if (_preparationService is null) return;
        var previous = _supplierSearchCancellation;
        var request = new CancellationTokenSource();
        _supplierSearchCancellation = request;
        previous?.Cancel();
        IsSupplierSearchBusy = true;
        try
        {
            var result = await _preparationService.SearchSuppliersAsync(new PagedRequestBase
            {
                Keyword = keyword?.Trim(),
                PageIndex = 1,
                PageSize = 50,
            }, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_supplierSearchCancellation, request)) return;
            SupplierCandidates.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var supplier in result.Data.Items) SupplierCandidates.Add(supplier);
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                StatusText = result.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_supplierSearchCancellation, request))
            {
                _supplierSearchCancellation = null;
                IsSupplierSearchBusy = false;
            }
            request.Dispose();
        }
    }

    [RelayCommand]
    private void CommitPurchaseSupplier(PurchaseCandidateItemModel? item) => item?.CommitPurchaseSupplier();

    [RelayCommand]
    private void ClearPurchaseSupplier(PurchaseCandidateItemModel? item) => item?.ClearPurchaseSupplier();

    [RelayCommand]
    private void CommitDeliverySupplier(PurchaseCandidateItemModel? item)
    {
        if (item?.IsCustomerSupplied == true) item.CommitDeliverySupplier();
    }

    [RelayCommand]
    private void ClearDeliverySupplier(PurchaseCandidateItemModel? item)
    {
        if (item?.IsCustomerSupplied == true) item.ClearDeliverySupplier();
    }

    /// <summary>把服务端结构化验证结果映射回稳定需求行，供只看错误和定位编辑使用。</summary>
    public void ApplyValidation(IReadOnlyList<PurchaseDraftWarningModel> warnings)
    {
        foreach (var item in Items) item.ValidationMessage = string.Empty;
        foreach (var warning in warnings.Where(static warning => warning.IsBlocker))
        {
            foreach (var item in Items.Where(item => warning.NetDemandIds.Contains(item.Uid)))
                item.ValidationMessage = string.IsNullOrWhiteSpace(item.ValidationMessage)
                    ? warning.Message
                    : $"{item.ValidationMessage}；{warning.Message}";
        }
        RefreshLocalFilter();
        SelectedItem = Items.FirstOrDefault(static item => item.HasValidationError);
    }

    /// <summary>加载完整采购候选来源分组，不自动选择或加载明细。</summary>
    private async Task LoadGroupsAsync(CancellationToken externalCancellation)
    {
        var request = ReplaceCancellation(ref _groupCancellation, externalCancellation);
        IsLoading = true;
        try
        {
            var result = await _runner.RunAsync(
                () => _api.GetCandidateSourceGroupsAsync(request.Token),
                request.Token,
                "加载待采购来源失败");
            if (!CanAccept(_groupCancellation, request)) return;

            if (!result.Success || result.Data is null)
            {
                SourceGroupStatusText = result.Message;
                StatusText = "待采购来源加载失败，右侧未加载数据。";
                return;
            }

            var total = result.Data.Sum(static item => item.CandidateCount);
            SourceGroups.Add(PurchaseCandidateSourceGroupModel.CreateAll(total));
            foreach (var group in result.Data)
                SourceGroups.Add(PurchaseCandidateSourceGroupModel.FromDto(group));

            SourceGroupStatusText = result.Data.Count == 0
                ? "当前没有待采购来源；可打开全部入口确认。"
                : $"共 {result.Data.Count} 张来源单，{total} 条待采购物料。";
            StatusText = "请选择左侧来源查看待采购物料。";
        }
        finally
        {
            CompleteRequest(ref _groupCancellation, request);
        }
    }

    /// <summary>按来源类型和 Uid 或全部入口一次完整加载候选，并拒绝过期结果回写。</summary>
    private async Task LoadCandidatesAsync(
        PurchaseCandidateSourceGroupModel group,
        CancellationToken externalCancellation)
    {
        var request = ReplaceCancellation(ref _detailCancellation, externalCancellation);
        ClearItems();
        IsDetailLoaded = false;
        SelectedSourceGroup = group;
        IsLoading = true;
        StatusText = $"正在加载 {group.DisplaySourceNo}...";
        try
        {
            var result = await _runner.RunAsync(
                () => _api.GetCandidatesAsync(new PurchaseDraftCandidateQuery
                {
                    SourceType = group.IsAllSources ? string.Empty : group.SourceType,
                    SourceUid = group.IsAllSources ? null : group.SourceUid,
                }, request.Token),
                request.Token,
                "加载待采购物料失败");
            if (!CanAccept(_detailCancellation, request)
                || !ReferenceEquals(SelectedSourceGroup, group))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }

            foreach (var dto in result.Data)
            {
                var row = PurchaseCandidateItemModel.FromDto(dto);
                row.PropertyChanged += OnRowPropertyChanged;
                row.ValueChanged += OnRowValueChanged;
                Items.Add(row);
            }
            IsDetailLoaded = true;
            RefreshLocalFilter();
        }
        finally
        {
            CompleteRequest(ref _detailCancellation, request);
        }
    }

    /// <summary>匹配当前已加载候选的关键词和来源号，不发起远程查询。</summary>
    private bool MatchesLocalFilter(object item)
    {
        if (item is not PurchaseCandidateItemModel row) return false;
        var keyword = Keyword.Trim();
        var sourceNo = SourceNo.Trim();
        return (!ShowOnlyErrors || row.HasValidationError)
            && (keyword.Length == 0
                || Contains(row.Sku, keyword)
                || Contains(row.LinkSku, keyword)
                || Contains(row.Description, keyword)
                || Contains(row.SuggestedPurchaseSupplierName, keyword)
                || Contains(row.SuggestedPurchaseSupplierCode, keyword))
            && (sourceNo.Length == 0 || Contains(row.SourceNo, sourceNo));
    }

    private static bool Contains(string? value, string filter) =>
        value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private List<PurchaseCandidateItemModel> GetVisibleRows() =>
        ItemsView.Cast<object>().OfType<PurchaseCandidateItemModel>().ToList();

    /// <summary>刷新集合视图并同步加载、筛选和选择计数。</summary>
    private void RefreshLocalFilter()
    {
        ItemsView.Refresh();
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        UpdateCountStatus();
    }

    private void UpdateCountStatus()
    {
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedCount));
        if (IsDetailLoaded && !IsLoading)
            StatusText = $"已加载 {Items.Count} 条，筛选后 {FilteredCount} 条，已选 {SelectedCount} 条。";
    }

    /// <summary>把行勾选变化提升为页面级全选和已选计数通知。</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PurchaseCandidateItemModel.IsChecked)) return;
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        UpdateCountStatus();
    }

    private void OnRowValueChanged(object? sender, EventArgs e)
    {
        if (sender is PurchaseCandidateItemModel row) row.ValidationMessage = string.Empty;
        InputChanged?.Invoke(this, EventArgs.Empty);
        UpdateCountStatus();
    }

    /// <summary>解除行事件并清空当前候选，防止跨来源保留选择。</summary>
    private void ClearItems()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnRowPropertyChanged;
            item.ValueChanged -= OnRowValueChanged;
        }
        Items.Clear();
        ItemsView.Refresh();
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedCount));
    }

    partial void OnKeywordChanged(string value) => RefreshLocalFilter();

    partial void OnSourceNoChanged(string value) => RefreshLocalFilter();

    partial void OnShowOnlyErrorsChanged(bool value) => RefreshLocalFilter();

    private static CancellationTokenSource ReplaceCancellation(
        ref CancellationTokenSource? owner,
        CancellationToken externalCancellation)
    {
        var previous = owner;
        owner = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        previous?.Cancel();
        return owner;
    }

    private bool CanAccept(CancellationTokenSource? owner, CancellationTokenSource request) =>
        !_disposed && !request.IsCancellationRequested && ReferenceEquals(owner, request);

    private void CompleteRequest(ref CancellationTokenSource? owner, CancellationTokenSource request)
    {
        if (ReferenceEquals(owner, request))
        {
            owner = null;
            IsLoading = false;
            UpdateCountStatus();
        }
        request.Dispose();
    }

    private void CancelPendingRequests()
    {
        var groups = _groupCancellation;
        var details = _detailCancellation;
        _groupCancellation = null;
        _detailCancellation = null;
        var supplierSearch = _supplierSearchCancellation;
        _supplierSearchCancellation = null;
        groups?.Cancel();
        details?.Cancel();
        supplierSearch?.Cancel();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>对话框关闭时取消查询并阻止旧结果回写；下一次打开会重新建立生命周期。</summary>
    public void Close()
    {
        CancelPendingRequests();
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
        IsLoading = false;
    }

    /// <summary>释放选择器持有的查询资源和行事件。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        ClearItems();
    }
}
