using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Sales.Fulfillment.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Sales.Fulfillment.ViewModels;

/// <summary>
/// 读取销售订单行级履约证据的只读页签。
/// </summary>
public sealed partial class SalesFulfillmentDetailViewModel(
    ISalesFulfillmentApi api, IApiCallRunner runner, INavigationService navigation,
    ICurrentUserService currentUser) : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;
    /// <summary>销售履约 Detail 的稳定路由键。</summary>
    public const string RouteKey = "sales-fulfillment-detail";

    /// <summary>当前履约页签关联的销售订单标识。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial Guid SalesOrderId { get; set; }

    /// <summary>当前订单的只读履约事实。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial SalesFulfillmentDetailDto? Detail { get; set; }

    /// <summary>当前展开的销售行。</summary>
    [ObservableProperty]
    public partial SalesFulfillmentLineDto? SelectedLine { get; set; }

    /// <summary>当前选中的采购证据。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPurchaseCommand))]
    public partial SalesFulfillmentPurchaseDto? SelectedPurchase { get; set; }

    /// <summary>当前选中的装箱证据。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPackingListCommand))]
    public partial SalesFulfillmentDeliveryDto? SelectedDelivery { get; set; }

    /// <summary>加载失败或空数据提示。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>采购详情仍受采购读取权限控制。</summary>
    public bool CanViewPurchase => currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);

    /// <summary>装箱详情仍受装箱查看权限控制。</summary>
    public bool CanViewPackingList => currentUser.HasPermission(PermissionKeys.ShippingPackingListView);

    /// <summary>加载后使用订单号作为页签标题。</summary>
    public string DocumentTitle => Detail is null ? $"销售履约详情 {SalesOrderId:N}" : $"履约 {Detail.OrderNo}";

    /// <summary>只读查询不维护可编辑状态。</summary>
    public bool HasUnsavedChanges => false;

    /// <summary>
    /// 接纳销售订单身份并加载当前履约事实；已有页签再次激活时可用刷新命令重查。
    /// </summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsReadOnly = true;
        SalesOrderId = parameter is SalesFulfillmentDetailOpenRequest request
            ? request.SalesOrderId
            : Guid.Empty;
        await LoadAsync(cancellationToken);
    }

    /// <summary>刷新货好修正及箱单反向后的最新事实。</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    /// <summary>替换旧查询并在结果回写前验证请求身份与页面存活。</summary>
    private async Task LoadAsync(CancellationToken navigationToken)
    {
        _loadCancellation?.Cancel();
        if (_disposed || SalesOrderId == Guid.Empty)
        {
            StatusText = "缺少销售订单标识。";
            return;
        }
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(navigationToken);
        _loadCancellation = requestCancellation;
        var orderId = SalesOrderId;
        await RunBusyAsync(async () =>
        {
            var result = await runner.RunAsync(
                () => api.GetAsync(orderId, requestCancellation.Token),
                requestCancellation.Token, "加载销售履约详情失败");
            if (_disposed || requestCancellation.IsCancellationRequested
                || !ReferenceEquals(_loadCancellation, requestCancellation) || SalesOrderId != orderId) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }
            Detail = result.Data;
            SelectedLine = Detail.Lines.FirstOrDefault();
            StatusText = Detail.Lines.Count == 0 ? "此订单没有有效销售行。" : string.Empty;
        }, "正在加载销售履约...", skipIfBusy: false);
        if (ReferenceEquals(_loadCancellation, requestCancellation)) _loadCancellation = null;
        requestCancellation.Dispose();
    }

    partial void OnSelectedLineChanged(SalesFulfillmentLineDto? value)
    {
        SelectedPurchase = null;
        SelectedDelivery = null;
    }

    /// <summary>打开有明确身份且当前用户可读的采购详情。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPurchase))]
    private Task OpenPurchaseAsync() => navigation.OpenDocumentAsync(
        PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedPurchase!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedPurchase.PurchaseOrderId });

    private bool CanOpenPurchase() => CanViewPurchase && SelectedPurchase is not null;

    /// <summary>打开有明确身份且当前用户可读的装箱详情，其内可继续查看 Trace。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPackingList))]
    private Task OpenPackingListAsync() => navigation.OpenDocumentAsync(
        PackingListDetailViewModel.RouteKey,
        SelectedDelivery!.PackingListId.ToString("N"),
        new PackingListOpenRequest { Uid = SelectedDelivery.PackingListId });

    private bool CanOpenPackingList() => CanViewPackingList && SelectedDelivery is not null;

    /// <summary>只读页面没有未保存数据，始终允许关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    /// <summary>关闭页签时取消在途查询，阻止离开后回写。</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
