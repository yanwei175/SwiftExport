using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Sales.Fulfillment.Models;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Sales.Fulfillment.ViewModels;

/// <summary>销售履约 Detail；只读展示来源、正式出库和采购关联，并提供受路由权限复核的详情导航。</summary>
public sealed partial class SalesFulfillmentDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "sales-fulfillment-detail";
    private readonly ISalesFulfillmentDetailApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _loadCts;
    private long _loadSequence;
    private bool _disposed;

    public SalesFulfillmentDetailViewModel(
        ISalesFulfillmentDetailApi api,
        IApiCallRunner runner,
        INavigationService navigation)
    {
        _api = api;
        _runner = runner;
        _navigation = navigation;
        Title = "销售履约详情";
        IsReadOnly = true;
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(OpenSalesOrderCommand);
        RegisterBusyAwareCommand(OpenPurchaseOrderCommand);
    }

    public ObservableCollection<SalesFulfillmentDetailLineDto> Lines { get; } = [];
    public ObservableCollection<SalesFulfillmentPurchaseDto> SelectedLinePurchases { get; } = [];
    public ObservableCollection<SalesFulfillmentPurchaseDto> OrderPurchases { get; } = [];
    public string DocumentTitle => string.IsNullOrWhiteSpace(Detail?.OrderNo) ? "销售履约详情" : $"履约 {Detail.OrderNo}";
    public bool HasUnsavedChanges => false;

    [ObservableProperty] public partial Guid SalesOrderId { get; set; }
    [ObservableProperty] public partial SalesFulfillmentDetailDto? Detail { get; set; }
    [ObservableProperty] public partial SalesFulfillmentDetailLineDto? SelectedLine { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "正在加载销售履约详情...";

    /// <summary>按稳定订单参数初始化文档；错误参数不会触发网络请求。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not SalesFulfillmentDetailOpenRequest request || request.SalesOrderId == Guid.Empty)
        {
            StatusText = "销售订单标识无效。";
            return;
        }
        SalesOrderId = request.SalesOrderId;
        await LoadAsync(cancellationToken);
    }

    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    [RelayCommand(CanExecute = nameof(CanOpenSalesOrder))]
    private Task OpenSalesOrderAsync() => _navigation.OpenDocumentAsync(
        SalesOrderManagerViewModel.DetailRouteKey,
        $"sales-order:{SalesOrderId:N}",
        new SalesOrderDocumentOpenRequest(SalesOrderId));

    [RelayCommand(CanExecute = nameof(CanOpenPurchaseOrder))]
    private Task OpenPurchaseOrderAsync(SalesFulfillmentPurchaseDto? purchase)
        => purchase is null
            ? Task.CompletedTask
            : _navigation.OpenDocumentAsync(
                PurchaseOrderDetailViewModel.RouteKey,
                $"purchase-order:{purchase.PurchaseOrderId:N}",
                new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = purchase.PurchaseOrderId, IsLegacy = false });

    partial void OnSelectedLineChanged(SalesFulfillmentDetailLineDto? value) => RebuildSelectedLinePurchases();

    partial void OnSalesOrderIdChanged(Guid value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        OpenSalesOrderCommand.NotifyCanExecuteChanged();
    }

    partial void OnDetailChanged(SalesFulfillmentDetailDto? value)
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OpenSalesOrderCommand.NotifyCanExecuteChanged();
    }

    /// <summary>加载完整 Detail 并仅在请求仍为最新且页面存活时回写 UI。</summary>
    private async Task LoadAsync(CancellationToken outerCancellation)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        _loadCts = requestCts;
        var sequence = Interlocked.Increment(ref _loadSequence);
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(
                () => _api.GetAsync(SalesOrderId, requestCts.Token),
                requestCts.Token,
                "加载销售履约详情失败");
            if (_disposed || requestCts.IsCancellationRequested || sequence != _loadSequence) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }
            Detail = result.Data;
            Lines.Clear();
            foreach (var line in result.Data.Lines) Lines.Add(line);
            OrderPurchases.Clear();
            foreach (var purchase in result.Data.OrderPurchases) OrderPurchases.Add(purchase);
            SelectedLine = Lines.FirstOrDefault();
            RebuildSelectedLinePurchases();
            StatusText = "当前已发量采用 SO-003 正式客户出库兼容口径；缺少来源需求时数量显示为未知。";
        }, "正在加载销售履约详情...", false);
    }

    /// <summary>按选中销售行过滤已聚合的组件采购，避免把订单级采购复制到每一行。</summary>
    private void RebuildSelectedLinePurchases()
    {
        SelectedLinePurchases.Clear();
        if (Detail is null || SelectedLine is null) return;
        foreach (var purchase in Detail.LinePurchases.Where(item => item.SalesOrderLineId == SelectedLine.SalesOrderLineId))
            SelectedLinePurchases.Add(purchase);
    }

    private bool CanRun() => IsNotBusy && !_disposed && SalesOrderId != Guid.Empty;
    private bool CanOpenSalesOrder() => CanRun() && Detail is not null;
    private bool CanOpenPurchaseOrder(SalesFulfillmentPurchaseDto? purchase) => CanRun() && purchase is not null;

    /// <summary>终止详情加载并使所有迟到响应失效。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _loadSequence);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
