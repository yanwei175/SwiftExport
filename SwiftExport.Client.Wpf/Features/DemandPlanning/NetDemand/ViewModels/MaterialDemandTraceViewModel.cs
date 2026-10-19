using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>以单条物料需求为锚点展示来源、拆解与采购收货的只读证据。</summary>
public sealed partial class MaterialDemandTraceViewModel(
    INetDemandManagerApi api, IApiCallRunner runner, INavigationService navigation,
    ICurrentUserService currentUser) : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    /// <summary>物料需求只读追踪路由。</summary>
    public const string RouteKey = "material-demand-trace";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial Guid NetDemandId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceTraceCommand))]
    public partial MaterialDemandTraceDetailDto? Detail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPurchaseCommand))]
    public partial MaterialDemandTracePurchaseDto? SelectedPurchase { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>页签标题使用物料与来源单号。</summary>
    public string DocumentTitle => Detail is null ? "物料需求追踪" : $"物料 {Detail.Sku} / {Detail.SourceNo}";

    /// <summary>只读页无未保存状态。</summary>
    public bool HasUnsavedChanges => false;

    /// <summary>按稳定物料行身份初始化追踪。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsReadOnly = true;
        NetDemandId = parameter is MaterialDemandTraceOpenRequest request ? request.NetDemandId : Guid.Empty;
        await LoadAsync(cancellationToken);
    }

    /// <summary>刷新采购反向和入库反向后的最新查询投影。</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    /// <summary>仅接纳最后一次仍在运行且页签存活的查询结果。</summary>
    private async Task LoadAsync(CancellationToken navigationToken)
    {
        _loadCancellation?.Cancel();
        if (_disposed || NetDemandId == Guid.Empty)
        {
            StatusText = "缺少物料需求标识。";
            return;
        }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(navigationToken);
        _loadCancellation = cancellation;
        var demandId = NetDemandId;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await runner.RunAsync(() => api.GetTraceAsync(demandId, cancellation.Token),
                    cancellation.Token, "加载物料需求追踪失败");
                if (_disposed || cancellation.IsCancellationRequested
                    || !ReferenceEquals(_loadCancellation, cancellation) || NetDemandId != demandId) return;
                if (!result.Success || result.Data is null)
                {
                    // 反向历史无法证明时清除旧覆盖，不能把刷新前的数量继续当作当前事实。
                    Detail = null;
                    SelectedPurchase = null;
                    StatusText = result.Message;
                    return;
                }
                Detail = result.Data;
                SelectedPurchase = null;
                StatusText = Detail.ReviewReason ?? string.Empty;
            }, "正在加载物料需求追踪...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>在订单跟踪查看权限允许时打开现有成品需求跟踪明细。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSourceTrace))]
    private Task OpenSourceTraceAsync() => navigation.OpenDocumentAsync(
        DemandTrackingDetailViewModel.RouteKey, Detail!.GrossDemandId.ToString("N"), Detail.GrossDemandId);

    private bool CanOpenSourceTrace() => Detail is not null
        && currentUser.HasPermission(PermissionKeys.OrderTrackingView);

    /// <summary>在采购读取权限允许且目标完整时打开采购详情。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPurchase))]
    private Task OpenPurchaseAsync() => navigation.OpenDocumentAsync(
        PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedPurchase!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedPurchase.PurchaseOrderId });

    private bool CanOpenPurchase() => SelectedPurchase?.HasPurchaseTarget == true
        && currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);

    /// <summary>只读页签可以直接关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    /// <summary>关闭时取消查询并释放请求资源。</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
