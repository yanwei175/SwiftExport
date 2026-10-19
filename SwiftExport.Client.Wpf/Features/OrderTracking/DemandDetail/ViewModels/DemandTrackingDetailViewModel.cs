using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;

/// <summary>拥有成品需求页签身份及基础事实，分别协调物料、采购、收货和交付区域的独立读取。</summary>
public sealed partial class DemandTrackingDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>仅通过总览打开的基础明细路由。</summary>
    public const string RouteKey = "demand-tracking-detail";
    private readonly IOrderTrackingApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _loadCancellation;
    private Guid _grossDemandId;
    private long _loadVersion;
    private bool _disposed;

    /// <summary>注入基础明细读取与独立查看权限，不依赖旧 Trace API。</summary>
    public DemandTrackingDetailViewModel(IOrderTrackingApi api, IApiCallRunner runner,
        INavigationService navigation, ICurrentUserService currentUser)
    {
        _api = api; _runner = runner; _navigation = navigation; _currentUser = currentUser;
        IsReadOnly = true;
        Title = "成品需求跟踪明细";
        Materials = new(runner, currentUser, [PermissionKeys.DemandPlanningMaterialView], api.GetMaterialsAsync,
            x => x.Count == 0, x => Reviews(x.Select(row => row.ReviewReason)));
        Purchases = new(runner, currentUser, [PermissionKeys.ProcurementOrderRead], api.GetPurchasesAsync,
            x => x.Count == 0, x => Reviews(x.Select(row => row.HasPurchaseTarget ? row.ReviewReason : "采购目标缺失，待核实。")));
        Receipts = new(runner, currentUser, [PermissionKeys.InventoryInboundView, PermissionKeys.ProcurementOrderRead], api.GetReceiptsAsync,
            x => x.Items.Count == 0, x => Reviews(x.Items.Select(row => row.CountsTowardReceipt is null && string.IsNullOrWhiteSpace(row.ReviewReason)
                ? "收货计入证据待核实。" : row.ReviewReason).Append(x.ReviewReason)), x => x.GrossDemandId);
        Deliveries = new(runner, currentUser, [PermissionKeys.ShippingPackingListView], api.GetDeliveriesAsync,
            x => x.Items.Count == 0, x => Reviews(x.Items.Select(row => row.ReviewReason).Append(x.ReviewReason)
                .Append(x.DeliveredQty is null || x.RemainingQty is null ? "交付数量证据待核实。" : null)), x => x.GrossDemandId);
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial DemandTrackingHeaderDto? Detail { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial int SelectedSectionIndex { get; set; }
    /// <summary>物料修订与快照区域。</summary>
    public DemandTrackingSectionViewModel<List<DemandTrackingMaterialDto>> Materials { get; }
    /// <summary>采购关联及整行事实区域。</summary>
    public DemandTrackingSectionViewModel<List<DemandTrackingPurchaseDto>> Purchases { get; }
    /// <summary>相关采购行收货证据区域，不代表本需求收货份额。</summary>
    public DemandTrackingSectionViewModel<DemandTrackingReceiptSectionDto> Receipts { get; }
    /// <summary>正式交付及反向历史区域。</summary>
    public DemandTrackingSectionViewModel<DemandTrackingDeliverySectionDto> Deliveries { get; }
    /// <summary>只使用独立订单跟踪查看权限。</summary>
    public bool CanView => !_disposed && _currentUser.HasPermission(PermissionKeys.OrderTrackingView);
    /// <inheritdoc />
    public string DocumentTitle => Detail is null ? "成品需求跟踪明细" : $"成品需求 · {Detail.SourceNo} / {Detail.SourceLineNumber}";
    /// <inheritdoc />
    public bool HasUnsavedChanges => false;
    /// <inheritdoc />
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>仅接受稳定成品需求 ID，拒绝来源单据身份或空身份。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not Guid id || id == Guid.Empty) throw new ArgumentException("缺少有效的成品需求身份。", nameof(parameter));
        _grossDemandId = id;
        Detail = null;
        Materials.Reset(id); Purchases.Reset(id); Receipts.Reset(id); Deliveries.Reset(id);
        await Task.WhenAll(LoadAsync(cancellationToken), EnsureSelectedSectionAsync(cancellationToken));
    }

    /// <summary>只加载被激活区域，保留其余区域的独立成功或失败状态。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadSelectedSectionAsync() => EnsureSelectedSectionAsync(CancellationToken.None);

    /// <summary>导航取消只传给该次初始化；切换区域不取消另一区域的独立加载。</summary>
    private Task EnsureSelectedSectionAsync(CancellationToken cancellationToken) => SelectedSectionIndex switch
    {
        1 => Materials.EnsureLoadedAsync(cancellationToken),
        2 => Purchases.EnsureLoadedAsync(cancellationToken),
        3 => Receipts.EnsureLoadedAsync(cancellationToken),
        4 => Deliveries.EnsureLoadedAsync(cancellationToken),
        _ => Task.CompletedTask
    };

    /// <summary>去重显示证据核实原因，不合并不同粒度的数量。</summary>
    private static string? Reviews(IEnumerable<string?> reasons)
    { var values = reasons.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray(); return values.Length == 0 ? null : string.Join("；", values); }

    /// <summary>独立刷新当前需求基础事实，允许较新的请求取消旧请求。</summary>
    [RelayCommand(CanExecute = nameof(CanView), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    /// <summary>激活保活总览，不创建第二份筛选或分页状态。</summary>
    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ReturnToOverviewAsync() => CanView ? _navigation.OpenAsync(OrderTrackingOverviewViewModel.RouteKey) : Task.CompletedTask;

    /// <summary>以请求版本、文档身份、会话及令牌保护明细，失败保留同一需求已显示的事实。</summary>
    private async Task LoadAsync(CancellationToken externalToken)
    {
        if (!CanView || _grossDemandId == Guid.Empty) return;
        var version = ++_loadVersion;
        _loadCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _loadCancellation = cancellation;
        var token = cancellation.Token;
        var id = _grossDemandId;
        var user = _currentUser.CurrentUser;
        IsLoading = true; ValidationSummary = null;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDemandHeaderAsync(id, token), token, "成品需求基础明细加载失败");
            if (token.IsCancellationRequested || version != _loadVersion || id != _grossDemandId || !CanView || !ReferenceEquals(user, _currentUser.CurrentUser)) return;
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                return;
            }
            if (result.Data.GrossDemandId != id)
            {
                ValidationSummary = "返回的成品需求身份不一致，请重新查询。";
                return;
            }
            Detail = result.Data;
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
        }
    }

    /// <summary>会话变化立即清除基础事实并取消旧请求。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        Materials.Reset(Guid.Empty); Purchases.Reset(Guid.Empty); Receipts.Reset(Guid.Empty); Deliveries.Reset(Guid.Empty);
        Detail = null; IsLoading = false; ValidationSummary = "会话已变更，请重新打开成品需求。";
        OnPropertyChanged(nameof(CanView));
        RefreshCommand.NotifyCanExecuteChanged(); ReturnToOverviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>页签关闭时解除会话订阅并取消读取。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_loadVersion;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        _loadCancellation?.Cancel();
        Materials.Dispose(); Purchases.Dispose(); Receipts.Dispose(); Deliveries.Dispose();
    }
}
