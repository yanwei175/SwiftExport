using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.Services;
using SwiftExportClient.Features.Sales.Fulfillment.Models;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Sales.Orders.ViewModels;

/// <summary>
/// 销售订单 Manager 页面模型，负责列表筛选、选中预览和复用 Detail API 的快捷命令。
/// </summary>
public sealed partial class SalesOrderManagerViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "sales-orders";
    public const string DetailRouteKey = "sales-order-detail";

    private readonly ISalesOrderManagerApi _managerApi;
    private readonly ISalesOrderDetailApi _detailApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISalesOrderCancellationDialogService _cancellationDialogService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _lineLoadCancellation;
    private bool _initialized;

    /// <summary>注入 Manager 查询、Detail 命令及页面导航和反馈依赖。</summary>
    public SalesOrderManagerViewModel(
        ISalesOrderManagerApi managerApi,
        ISalesOrderDetailApi detailApi,
        IApiCallRunner apiCallRunner,
        INavigationService navigationService,
        IDialogService dialogService,
        ISalesOrderCancellationDialogService cancellationDialogService,
        IMessageService messageService)
    {
        _managerApi = managerApi;
        _detailApi = detailApi;
        _apiCallRunner = apiCallRunner;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _cancellationDialogService = cancellationDialogService;
        _messageService = messageService;
        Title = "销售订单管理";

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(OpenNewCommand);
        RegisterBusyAwareCommand(OpenSelectedCommand);
        RegisterBusyAwareCommand(OpenFulfillmentDetailCommand);
        RegisterBusyAwareCommand(ConfirmSelectedCommand);
        RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(CloseSelectedCommand);
        RegisterBusyAwareCommand(DeleteDraftCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<SalesOrderManagerListItemDto> Orders { get; } = [];
    public ObservableCollection<SalesOrderDetailLineDto> SelectedOrderLines { get; } = [];
    public ObservableCollection<string> CustomerCodeOptions { get; } = [];
    public IReadOnlyList<SalesOrderFilterOption<SalesOrderDocumentStatusEnum>> DocumentStatusOptions { get; } =
    [
        new(SalesOrderDocumentStatusEnum.Open, "正常"),
        new(SalesOrderDocumentStatusEnum.Closed, "已关闭"),
    ];
    public IReadOnlyList<SalesOrderFilterOption<ConfirmationStatusEnum>> ConfirmationStatusOptions { get; } =
    [
        new(ConfirmationStatusEnum.待确认, "待确认"),
        new(ConfirmationStatusEnum.已确认, "已确认"),
    ];
    public ObservableCollection<SalesOrderFilterOption<SalesOrderDocumentStatusEnum>> SelectedDocumentStatuses { get; } = [];
    public ObservableCollection<SalesOrderFilterOption<ConfirmationStatusEnum>> SelectedConfirmationStatuses { get; } = [];
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPoNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string InvoiceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSkusText { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? OrderDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? OrderDateTo { get; set; }
    [ObservableProperty] public partial DateTime? DeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? DeliveryDateTo { get; set; }

    /// <summary>
    /// 获取或设置高级筛选 Popup 是否打开，仅控制筛选区域显隐，不改变当前筛选值。
    /// </summary>
    [ObservableProperty]
    public partial bool IsAdvancedFilterOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFulfillmentDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelConfirmationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDraftCommand))]
    public partial SalesOrderManagerListItemDto? SelectedOrder { get; set; }

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

    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载销售订单。";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await Task.WhenAll(LoadCustomerCodesAsync(), LoadPageAsync());
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null)
        {
            Keyword = query;
        }

        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadPageAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        Keyword = string.Empty;
        CustomerCode = string.Empty;
        OrderNo = string.Empty;
        CustomerPoNo = string.Empty;
        InvoiceNo = string.Empty;
        CustomerSkusText = string.Empty;
        OrderDateFrom = null;
        OrderDateTo = null;
        DeliveryDateFrom = null;
        DeliveryDateTo = null;
        SelectedDocumentStatuses.Clear();
        SelectedConfirmationStatuses.Clear();
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task OpenNewAsync()
    {
        var instanceId = Guid.NewGuid();
        return _navigationService.OpenDocumentAsync(
            DetailRouteKey,
            $"sales-order:new:{instanceId:N}",
            new SalesOrderDocumentOpenRequest(null));
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private Task OpenSelectedAsync()
    {
        if (SelectedOrder is null)
        {
            return Task.CompletedTask;
        }

        return _navigationService.OpenDocumentAsync(
            DetailRouteKey,
            $"sales-order:{SelectedOrder.Uid:N}",
            new SalesOrderDocumentOpenRequest(SelectedOrder.Uid));
    }

    /// <summary>从销售管理页按稳定文档键打开只读履约 Detail，重复打开时激活已有页签。</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private Task OpenFulfillmentDetailAsync()
    {
        var order = SelectedOrder;
        return order is null
            ? Task.CompletedTask
            : _navigationService.OpenDocumentAsync(
                SalesFulfillmentDetailViewModel.RouteKey,
                $"sales-fulfillment-detail:{order.Uid:N}",
                new SalesFulfillmentDetailOpenRequest(order.Uid));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadSelectedLinesAsync()
    {
        _lineLoadCancellation?.Cancel();
        var requestCancellation = new CancellationTokenSource();
        _lineLoadCancellation = requestCancellation;
        var selected = SelectedOrder;
        SelectedOrderLines.Clear();
        if (selected is null)
        {
            if (ReferenceEquals(_lineLoadCancellation, requestCancellation))
            {
                _lineLoadCancellation = null;
            }

            requestCancellation.Dispose();
            return;
        }

        var cancellationToken = requestCancellation.Token;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.GetAsync(selected.Uid, cancellationToken),
                cancellationToken,
                "加载销售订单明细失败");
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(selected, SelectedOrder))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ResultSummary = result.Message;
                return;
            }

            foreach (var line in result.Data.Lines)
            {
                SelectedOrderLines.Add(line);
            }
        }
        finally
        {
            if (ReferenceEquals(_lineLoadCancellation, requestCancellation))
            {
                _lineLoadCancellation = null;
            }

            requestCancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmSelectedAsync()
    {
        var order = SelectedOrder;
        if (order is null || !await _dialogService.ConfirmAsync(
                "确认销售订单",
                $"确认订单 {order.OrderNo} 后将生成毛需求，但不会拆解 BOM。是否继续？",
                "确认"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.ConfirmAsync(order.Uid, CreateConfirmCommand(order)),
                "确认销售订单失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("确认销售订单失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("销售订单已确认", $"已生成 {result.Data.GrossDemandCount} 条毛需求。等待需求工作台拆解。");
            await LoadPageAsync();
        }, "正在确认销售订单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        var order = SelectedOrder;
        if (order is null)
        {
            return;
        }
        var reason = await _cancellationDialogService.RequestReasonAsync(order.OrderNo);
        if (reason is null) return;

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.CancelConfirmationAsync(order.Uid, CreateCancelConfirmationCommand(order, reason)),
                "取消销售订单确认失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("取消确认失败", result.Message);
                return;
            }

            _messageService.ShowSuccess(
                "已取消确认",
                $"{order.OrderNo}：删除 {result.Data.HardDeletedGrossDemandCount} 条，作废保留 {result.Data.VoidedGrossDemandCount} 条毛需求。");
            await LoadPageAsync();
        }, "正在取消确认...");
    }

    [RelayCommand(CanExecute = nameof(CanCloseOrder))]
    private async Task CloseSelectedAsync()
    {
        var order = SelectedOrder;
        if (order is null || !await _dialogService.ConfirmAsync(
                "关闭销售订单",
                $"关闭订单 {order.OrderNo} 后将保留全部历史，但不能继续编辑。是否继续？",
                "关闭"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.CloseAsync(order.Uid, new CloseSalesOrderRequest
                {
                    Uid = order.Uid,
                    Version = order.Version,
                    Reason = "用户从销售订单管理页关闭",
                }),
                "关闭销售订单失败");
            if (!result.Success)
            {
                _messageService.ShowError("关闭销售订单失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("销售订单已关闭", order.OrderNo);
            await LoadPageAsync();
        }, "正在关闭销售订单...");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDraft))]
    private async Task DeleteDraftAsync()
    {
        var order = SelectedOrder;
        if (order is null || !await _dialogService.ConfirmAsync(
                "删除销售订单草稿",
                $"确定删除未确认草稿 {order.OrderNo} 吗？",
                "删除"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.DeleteDraftAsync(order.Uid, CreateCommandRequest(order)),
                "删除销售订单草稿失败");
            if (!result.Success)
            {
                _messageService.ShowError("删除草稿失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("销售订单草稿已删除", order.OrderNo);
            await LoadPageAsync();
        }, "正在删除销售订单草稿...");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null)
        {
            return Task.CompletedTask;
        }

        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        return LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        await RunBusyAsync(async () =>
        {
            _lineLoadCancellation?.Cancel();
            _lineLoadCancellation = null;
            SelectedOrderLines.Clear();
            var result = await _apiCallRunner.RunAsync(
                () => _managerApi.SearchAsync(CreateSearchRequest()),
                "查询销售订单失败");
            if (!result.Success || result.Data is null)
            {
                Orders.Clear();
                SelectedOrder = null;
                TotalCount = 0;
                ResultSummary = result.Message;
                return;
            }

            Orders.Clear();
            foreach (var order in result.Data.Items)
            {
                Orders.Add(order);
            }

            TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex <= 0 ? PageIndex : result.Data.PageIndex;
            SelectedOrder = Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张销售订单，本页 {Orders.Count} 张。";
        }, "正在加载销售订单...");
    }

    private async Task LoadCustomerCodesAsync()
    {
        var result = await _apiCallRunner.RunAsync(
            () => _managerApi.GetCustomerCodesAsync(),
            "加载客户代码建议失败");
        if (!result.Success || result.Data is null)
        {
            return;
        }

        CustomerCodeOptions.Clear();
        foreach (var code in result.Data)
        {
            CustomerCodeOptions.Add(code);
        }
    }

    private SalesOrderManagerSearchRequest CreateSearchRequest()
    {
        var request = new SalesOrderManagerSearchRequest
        {
            Keyword = Keyword.Trim(),
            OrderNo = OrderNo.Trim(),
            CustomerPoNo = CustomerPoNo.Trim(),
            InvoiceNo = InvoiceNo.Trim(),
            OrderDateFrom = OrderDateFrom,
            OrderDateTo = OrderDateTo,
            DeliveryDateFrom = DeliveryDateFrom,
            DeliveryDateTo = DeliveryDateTo,
            PageIndex = PageIndex,
            PageSize = PageSize,
            CustomerSkus = SplitValues(CustomerSkusText),
            CustomerCodes = string.IsNullOrWhiteSpace(CustomerCode)
                ? []
                : [CustomerCode.Trim()],
            DocumentStatuses = SelectedDocumentStatuses.Select(option => option.Value).Distinct().ToList(),
            ConfirmationStatuses = SelectedConfirmationStatuses.Select(option => option.Value).Distinct().ToList(),
        };
        return request;
    }

    private static List<string> SplitValues(string text)
    {
        return text.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SalesOrderCommandRequest CreateCommandRequest(SalesOrderManagerListItemDto order)
    {
        return new SalesOrderCommandRequest { Uid = order.Uid, Version = order.Version };
    }

    /// <summary>为一次销售确认生成非空幂等命令标识；网络层重试复用同一请求对象。</summary>
    private static ConfirmSalesOrderCommand CreateConfirmCommand(SalesOrderManagerListItemDto order)
    {
        return new ConfirmSalesOrderCommand { Uid = order.Uid, Version = order.Version, CommandId = Guid.NewGuid() };
    }

    private static CancelSalesOrderConfirmationCommand CreateCancelConfirmationCommand(
        SalesOrderManagerListItemDto order,
        string reason)
    {
        return new CancelSalesOrderConfirmationCommand
        {
            Uid = order.Uid,
            Version = order.Version,
            CommandId = Guid.NewGuid(),
            Reason = reason
        };
    }

    private bool CanRun() => IsNotBusy;
    private bool CanUseSelected() => IsNotBusy && SelectedOrder is not null;
    private bool CanConfirm() => CanWrite && IsNotBusy && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.待确认,
    };
    private bool CanCancelConfirmation() => CanWrite && IsNotBusy && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.已确认,
    };
    private bool CanCloseOrder() => CanWrite && IsNotBusy && SelectedOrder?.DocumentStatus == SalesOrderDocumentStatusEnum.Open;
    private bool CanDeleteDraft() => CanWrite && IsNotBusy && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.待确认,
    };
}
