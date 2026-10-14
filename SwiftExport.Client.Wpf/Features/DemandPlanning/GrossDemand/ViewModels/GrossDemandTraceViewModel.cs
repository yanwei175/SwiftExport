using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

/// <summary>按单条成品需求展示拆解、采购关联及装箱交付的只读证据。</summary>
public sealed partial class GrossDemandTraceViewModel(
    IGrossDemandWorkspaceApi api, IApiCallRunner runner, INavigationService navigation,
    ICurrentUserService currentUser) : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    /// <summary>成品需求只读追踪路由键。</summary>
    public const string RouteKey = "gross-demand-trace";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial Guid GrossDemandId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceCommand))]
    public partial GrossDemandTraceDetailDto? Detail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPurchaseCommand))]
    public partial GrossDemandTracePurchaseDto? SelectedPurchase { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPackingListCommand))]
    public partial GrossDemandTraceDeliveryDto? SelectedDelivery { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>页签标题使用来源单号和来源行号。</summary>
    public string DocumentTitle => Detail is null ? "成品需求追踪" : $"来源 {Detail.SourceNo} / {Detail.SourceLineNumber}";

    /// <summary>只读追踪无未保存状态。</summary>
    public bool HasUnsavedChanges => false;

    /// <summary>使用稳定成品需求身份初始化页签。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsReadOnly = true;
        GrossDemandId = parameter is GrossDemandTraceOpenRequest request ? request.GrossDemandId : Guid.Empty;
        await LoadAsync(cancellationToken);
    }

    /// <summary>刷新反向、采购反馈和拆解修订后的最新证据。</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    /// <summary>替换在途查询，并防止已关闭页签或旧请求回写。</summary>
    private async Task LoadAsync(CancellationToken navigationToken)
    {
        _loadCancellation?.Cancel();
        if (_disposed || GrossDemandId == Guid.Empty)
        {
            StatusText = "缺少成品需求标识。";
            return;
        }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(navigationToken);
        _loadCancellation = cancellation;
        var demandId = GrossDemandId;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await runner.RunAsync(
                    () => api.GetTraceAsync(demandId, cancellation.Token), cancellation.Token,
                    "加载成品需求追踪失败");
                if (_disposed || cancellation.IsCancellationRequested
                    || !ReferenceEquals(_loadCancellation, cancellation) || GrossDemandId != demandId) return;
                if (!result.Success || result.Data is null)
                {
                    // 本次历史核实失败时清除旧投影，避免刷新后继续显示过期的确定数量。
                    Detail = null;
                    SelectedPurchase = null;
                    SelectedDelivery = null;
                    StatusText = result.Message;
                    return;
                }
                Detail = result.Data;
                SelectedPurchase = null;
                SelectedDelivery = null;
                StatusText = Detail.ReviewReason ?? string.Empty;
            }, "正在加载成品需求追踪...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>在采购读取权限允许时打开关联采购单。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPurchase))]
    private Task OpenPurchaseAsync() => navigation.OpenDocumentAsync(
        PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedPurchase!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedPurchase.PurchaseOrderId });

    private bool CanOpenPurchase() => SelectedPurchase?.HasPurchaseTarget == true
        && currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);

    /// <summary>仅在来源类型和当前读取权限均明确时打开上游销售或售后单据。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSource))]
    private Task OpenSourceAsync()
    {
        if (Detail!.SourceType == "SO")
            return navigation.OpenDocumentAsync(
                SalesOrderManagerViewModel.DetailRouteKey,
                $"sales-order:{Detail.SourceUid:N}",
                new SalesOrderDocumentOpenRequest(Detail.SourceUid));
        return navigation.OpenDocumentAsync(
            AfterSalesDetailViewModel.RouteKey,
            $"after-sales:{Detail.SourceUid:N}",
            new AfterSalesDocumentOpenRequest { AfterSalesId = Detail.SourceUid });
    }

    private bool CanOpenSource() => Detail is not null &&
        (Detail.SourceType == "SO" && currentUser.HasPermission(PermissionKeys.SalesOrderView)
         || Detail.SourceType == "CR" && currentUser.HasPermission(PermissionKeys.QualityControlAfterSalesView));

    /// <summary>在装箱读取权限允许时打开关联箱单。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPackingList))]
    private Task OpenPackingListAsync() => navigation.OpenDocumentAsync(
        PackingListDetailViewModel.RouteKey,
        SelectedDelivery!.PackingListId.ToString("N"),
        new PackingListOpenRequest { Uid = SelectedDelivery.PackingListId });

    private bool CanOpenPackingList() => SelectedDelivery is not null
        && currentUser.HasPermission(PermissionKeys.ShippingPackingListView);

    /// <summary>只读页签允许直接关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    /// <summary>关闭时取消在途查询并释放请求资源。</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
