using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Sales.Fulfillment.Models;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Sales.Fulfillment.ViewModels;

/// <summary>销售履约 Manager；负责订单级筛选、分页及打开只读 Detail，不加载详情全集。</summary>
public sealed partial class SalesFulfillmentManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "sales-fulfillment-manager";
    private readonly ISalesFulfillmentManagerApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _searchCts;
    private long _searchSequence;
    private bool _initialized;
    private bool _disposed;

    public SalesFulfillmentManagerViewModel(
        ISalesFulfillmentManagerApi api,
        IApiCallRunner runner,
        INavigationService navigation)
    {
        _api = api;
        _runner = runner;
        _navigation = navigation;
        Title = "销售履约查询";
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(OpenDetailCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<SalesFulfillmentManagerListItemDto> Orders { get; } = [];
    public IReadOnlyList<SalesOrderFilterOption<SalesOrderFulfillmentStatusEnum>> FulfillmentStatusOptions { get; } =
    [
        new(SalesOrderFulfillmentStatusEnum.NotStarted, "未发货"),
        new(SalesOrderFulfillmentStatusEnum.PartiallyShipped, "部分发货"),
        new(SalesOrderFulfillmentStatusEnum.FullyShipped, "全部发货")
    ];
    public ObservableCollection<SalesOrderFilterOption<SalesOrderFulfillmentStatusEnum>> SelectedFulfillmentStatuses { get; } = [];
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    public partial SalesFulfillmentManagerListItemDto? SelectedOrder { get; set; }
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
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载履约订单。";

    /// <summary>首次进入页面时加载第一页；重复激活不会发起重复请求。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync()
    {
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadPageAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        Keyword = OrderNo = CustomerCode = string.Empty;
        SelectedFulfillmentStatuses.Clear();
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync()
    {
        var order = SelectedOrder;
        return order is null
            ? Task.CompletedTask
            : _navigation.OpenDocumentAsync(
                SalesFulfillmentDetailViewModel.RouteKey,
                $"sales-fulfillment-detail:{order.SalesOrderId:N}",
                new SalesFulfillmentDetailOpenRequest(order.SalesOrderId));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        return LoadPageAsync();
    }

    /// <summary>用独立 CTS 和请求序号实现 latest-request-wins，页面释放后不回写集合。</summary>
    private async Task LoadPageAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var requestCts = new CancellationTokenSource();
        _searchCts = requestCts;
        var sequence = Interlocked.Increment(ref _searchSequence);
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(
                () => _api.SearchAsync(new SalesFulfillmentManagerSearchRequest
                {
                    Keyword = Keyword.Trim(), OrderNo = OrderNo.Trim(), CustomerCode = CustomerCode.Trim(),
                    PageIndex = PageIndex, PageSize = PageSize,
                    FulfillmentStatuses = SelectedFulfillmentStatuses.Select(item => item.Value).Distinct().ToList()
                }, requestCts.Token),
                requestCts.Token,
                "查询销售履约失败");
            if (_disposed || requestCts.IsCancellationRequested || sequence != _searchSequence) return;
            Orders.Clear();
            if (!result.Success || result.Data is null)
            {
                SelectedOrder = null;
                TotalCount = 0;
                ResultSummary = result.Message;
                return;
            }
            foreach (var order in result.Data.Items) Orders.Add(order);
            TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex;
            SelectedOrder = Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张销售订单；当前采用 SO-003 正式出库兼容口径。";
        }, "正在加载销售履约...", false);
    }

    private bool CanRun() => IsNotBusy && !_disposed;
    private bool CanOpenDetail() => CanRun() && SelectedOrder is not null;

    /// <summary>取消页面持有的搜索请求，确保导航离开后不再提交结果。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _searchSequence);
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }
}
