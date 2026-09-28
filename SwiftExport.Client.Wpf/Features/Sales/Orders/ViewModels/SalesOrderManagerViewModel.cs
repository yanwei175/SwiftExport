using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
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
using SwiftExportClient.Authentication;

namespace SwiftExportClient.Features.Sales.Orders.ViewModels;

/// <summary>
/// 销售订单 Manager 页面模型，负责列表筛选、选中预览和复用 Detail API 的快捷命令。
/// </summary>
public sealed partial class SalesOrderManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel
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
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _lineLoadCancellation;
    private bool _initialized;
    // 不同订单的未决确认各自保留原请求，列表刷新和切换选中项不能改变命令身份。
    private readonly Dictionary<Guid, ConfirmSalesOrderCommand> _pendingConfirmations = [];
    // 按命令身份隔离未知结果，切单不继承其他命令的状态。
    private readonly HashSet<Guid> _unknownConfirmationIds = [];
    private PendingManagerAction? _pendingAction;

    /// <summary>注入 Manager 查询、Detail 命令及页面导航和反馈依赖。</summary>
    public SalesOrderManagerViewModel(
        ISalesOrderManagerApi managerApi,
        ISalesOrderDetailApi detailApi,
        IApiCallRunner apiCallRunner,
        INavigationService navigationService,
        IDialogService dialogService,
        ISalesOrderCancellationDialogService cancellationDialogService,
        IMessageService messageService,
        ICurrentUserService currentUser)
    {
        _managerApi = managerApi;
        _detailApi = detailApi;
        _apiCallRunner = apiCallRunner;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _cancellationDialogService = cancellationDialogService;
        _messageService = messageService;
        _currentUser = currentUser;
        Title = "销售订单管理";
        PropertyChanged += OnBusyPropertyChanged;

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(OpenNewCommand);
        RegisterBusyAwareCommand(OpenSelectedCommand);
        RegisterBusyAwareCommand(OpenFulfillmentDetailCommand);
        RegisterBusyAwareCommand(ConfirmSelectedCommand);
        RegisterBusyAwareCommand(RetryPendingConfirmationCommand);
        RegisterBusyAwareCommand(RetryPendingActionCommand);
        RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(CloseSelectedCommand);
        RegisterBusyAwareCommand(DeleteDraftCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<SalesOrderManagerListItemDto> Orders { get; } = [];
    /// <summary>管理页仍以固定标题显示；未决确认借用页签关闭门禁保留重试入口。</summary>
    public string DocumentTitle => "销售订单管理";
    /// <summary>任一订单结果未知时禁止普通关闭管理页，以免丢失原命令身份。</summary>
    public bool HasUnsavedChanges => HasPendingIntent;
    /// <summary>待确认或待重试写入期间停止筛选、切单和页面关闭。</summary>
    public bool HasPendingIntent => _pendingConfirmations.Count > 0 || _pendingAction is not null;
    /// <summary>管理列表在未决命令期间冻结选择和刷新入口。</summary>
    public bool CanBrowse => !HasPendingIntent && IsNotBusy;

    /// <summary>Busy 结束后重新通知列表绑定；未决状态在 Busy 内清除时不能留下禁用的表格。</summary>
    private void OnBusyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsBusy)) OnPropertyChanged(nameof(CanBrowse));
    }
    /// <summary>维护权限独立于销售导航权限。</summary>
    public bool CanMaintain => _currentUser.HasPermission(PermissionKeys.SalesOrderMaintain);
    /// <summary>草稿删除使用独立授权，与保存维护能力分开。</summary>
    public bool CanDeleteOrderDraft => _currentUser.HasPermission(PermissionKeys.SalesOrderDelete);
    /// <summary>正向确认权限独立于取消确认权限。</summary>
    public bool CanConfirmOrder => _currentUser.HasPermission(PermissionKeys.SalesOrderConfirm);
    /// <summary>取消确认权限独立于正向确认权限。</summary>
    public bool CanCancelOrderConfirmation => _currentUser.HasPermission(PermissionKeys.SalesOrderCancelConfirmation);
    /// <summary>关闭权限独立于维护权限。</summary>
    public bool CanCloseOrderLifecycle => _currentUser.HasPermission(PermissionKeys.SalesOrderLifecycle);
    /// <summary>只有全部确认意图已取得确定结果，才允许关闭当前页面实例。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken)
        => Task.FromResult(!IsBusy && !HasPendingIntent);
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
        if (order is null || (!_pendingConfirmations.ContainsKey(order.Uid) && !await _dialogService.ConfirmAsync(
                "确认销售订单",
                $"确认订单 {order.OrderNo} 后将生成毛需求，但不会拆解 BOM。是否继续？",
                "确认")))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            if (!_pendingConfirmations.TryGetValue(order.Uid, out var request))
                _pendingConfirmations[order.Uid] = request = CreateConfirmCommand(order);
            NotifyPendingStateChanged();
            RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.ConfirmAsync(request.Uid, request),
                "确认销售订单失败");
            if (!result.Success || result.Data is null)
            {
                if (!MustKeepConfirmationIntent(request.CommandId, result.Error?.Code))
                    _pendingConfirmations.Remove(order.Uid);
                NotifyPendingStateChanged();
                RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
                _messageService.ShowError("确认销售订单失败", result.Message);
                return;
            }

            _pendingConfirmations.Remove(order.Uid);
            _unknownConfirmationIds.Remove(request.CommandId);
            NotifyPendingStateChanged();
            RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
            _messageService.ShowSuccess("销售订单已确认", $"已生成 {result.Data.GrossDemandCount} 条毛需求。等待需求工作台拆解。");
            await LoadPageCoreAsync();
        }, "正在确认销售订单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        var order = SelectedOrder;
        if (order is null || HasPendingIntent)
        {
            return;
        }
        var reason = await _cancellationDialogService.RequestReasonAsync(order.OrderNo);
        if (reason is null) return;
        SetPendingAction(new PendingManagerAction("cancel", order.Uid, order.OrderNo,
            CreateCancelConfirmationCommand(order, reason)));
        await ExecutePendingActionAsync();
    }

    /// <summary>列表筛选或切单后仍可按保存的原目标、版本和命令标识重试最早的未决确认。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryPendingConfirmation))]
    private async Task RetryPendingConfirmationAsync()
    {
        if (_pendingConfirmations.Count == 0) return;
        var request = _pendingConfirmations.Values.First();
        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _detailApi.ConfirmAsync(request.Uid, request), "重试销售订单确认失败");
            if (!result.Success || result.Data is null)
            {
                if (!MustKeepConfirmationIntent(request.CommandId, result.Error?.Code))
                    _pendingConfirmations.Remove(request.Uid);
                NotifyPendingStateChanged();
                RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
                _messageService.ShowError("重试销售订单确认失败", result.Message);
                return;
            }
            _pendingConfirmations.Remove(request.Uid);
            _unknownConfirmationIds.Remove(request.CommandId);
            NotifyPendingStateChanged();
            RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
            _messageService.ShowSuccess("销售订单已确认", $"已生成 {result.Data.GrossDemandCount} 条毛需求。");
            await LoadPageCoreAsync();
        }, "正在核实销售订单确认...");
    }

    [RelayCommand(CanExecute = nameof(CanCloseOrder))]
    private async Task CloseSelectedAsync()
    {
        var order = SelectedOrder;
        if (order is null || HasPendingIntent || !await _dialogService.ConfirmAsync(
                "关闭销售订单",
                $"关闭订单 {order.OrderNo} 后将保留全部历史，但不能继续编辑。是否继续？",
                "关闭"))
        {
            return;
        }

        SetPendingAction(new PendingManagerAction("close", order.Uid, order.OrderNo,
            new CloseSalesOrderRequest { Uid = order.Uid, Version = order.Version,
                CommandId = Guid.NewGuid(), Reason = "用户从销售订单管理页关闭" }));
        await ExecutePendingActionAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDraft))]
    private async Task DeleteDraftAsync()
    {
        var order = SelectedOrder;
        if (order is null || HasPendingIntent || !await _dialogService.ConfirmAsync(
                "删除销售订单草稿",
                $"确定删除未确认草稿 {order.OrderNo} 吗？",
                "删除"))
        {
            return;
        }

        SetPendingAction(new PendingManagerAction("delete", order.Uid, order.OrderNo,
            CreateCommandRequest(order)));
        await ExecutePendingActionAsync();
    }

    /// <summary>待确认的取消、关闭或删除命令使用冻结的原目标、版本、原因和命令身份重试。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryPendingAction))]
    private Task RetryPendingActionAsync() => ExecutePendingActionAsync();

    private bool CanRetryPendingAction() => IsNotBusy && _pendingAction is not null;

    /// <summary>在页面实例中固定一个管理页写意图，发送中也阻止关闭和筛选。</summary>
    private void SetPendingAction(PendingManagerAction? pending)
    {
        _pendingAction = pending;
        NotifyPendingStateChanged();
    }

    /// <summary>执行冻结请求；确定失败解除意图，结果未知及后续前置拒绝保留原请求。</summary>
    private async Task ExecutePendingActionAsync()
    {
        if (_pendingAction is not { } pending) return;
        await RunWriteAsync(async () =>
        {
            if (pending.Request is CancelSalesOrderConfirmationCommand cancel)
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _detailApi.CancelConfirmationAsync(pending.Uid, cancel), "取消销售订单确认失败");
                if (!result.Success || result.Data is null)
                {
                    HandlePendingActionFailure(pending, result.Error?.Code, result.Message);
                    return;
                }
                SetPendingAction(null);
                _messageService.ShowSuccess("已取消确认",
                    $"{pending.OrderNo}：删除 {result.Data.HardDeletedGrossDemandCount} 条，作废保留 {result.Data.VoidedGrossDemandCount} 条毛需求。");
            }
            else if (pending.Request is CloseSalesOrderRequest close)
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _detailApi.CloseAsync(pending.Uid, close), "关闭销售订单失败");
                if (!result.Success)
                {
                    HandlePendingActionFailure(pending, result.Error?.Code, result.Message);
                    return;
                }
                SetPendingAction(null);
                _messageService.ShowSuccess("销售订单已关闭", pending.OrderNo);
            }
            else if (pending.Request is SalesOrderCommandRequest delete)
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _detailApi.DeleteDraftAsync(pending.Uid, delete), "删除销售订单草稿失败");
                if (!result.Success || !result.Data)
                {
                    HandlePendingActionFailure(pending, result.Error?.Code, result.Message);
                    return;
                }
                SetPendingAction(null);
                _messageService.ShowSuccess("销售订单草稿已删除", pending.OrderNo);
            }
            await LoadPageCoreAsync();
        }, "正在核实销售订单操作...");
    }

    /// <summary>限流与无权仅说明本次重试未执行，不能推断此前未知意图的结果。</summary>
    private void HandlePendingActionFailure(PendingManagerAction pending, string? code, string message)
    {
        pending.OutcomeUnknown |= code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy
            or ServiceErrorCodes.ServiceUnavailable;
        var keep = pending.OutcomeUnknown && code is ServiceErrorCodes.OutcomeUnknown
            or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable
            or ServiceErrorCodes.RateLimited or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden;
        if (!keep) SetPendingAction(null);
        _messageService.ShowError("销售订单操作失败", message);
    }

    /// <summary>通知列表冻结、页签关闭及所有操作和重试命令。</summary>
    private void NotifyPendingStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasPendingIntent));
        OnPropertyChanged(nameof(CanBrowse));
        RetryPendingConfirmationCommand.NotifyCanExecuteChanged();
        RetryPendingActionCommand.NotifyCanExecuteChanged();
        ConfirmSelectedCommand.NotifyCanExecuteChanged();
        CancelConfirmationCommand.NotifyCanExecuteChanged();
        CloseSelectedCommand.NotifyCanExecuteChanged();
        DeleteDraftCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>管理页非确认操作的原始请求与结果未知状态。</summary>
    private sealed class PendingManagerAction(string operation, Guid uid, string orderNo, object request)
    {
        public string Operation { get; } = operation;
        public Guid Uid { get; } = uid;
        public string OrderNo { get; } = orderNo;
        public object Request { get; } = request;
        public bool OutcomeUnknown { get; set; }
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
        await RunBusyAsync(LoadPageCoreAsync, "正在加载销售订单...");
    }

    /// <summary>供独立查询和确认写命令共用的实际列表刷新，避免嵌套 Busy 跳过刷新。</summary>
    private async Task LoadPageCoreAsync()
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
        return new SalesOrderCommandRequest { Uid = order.Uid, Version = order.Version, CommandId = Guid.NewGuid() };
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

    private bool CanRun() => CanBrowse;
    private bool CanUseSelected() => CanBrowse && SelectedOrder is not null;
    private bool CanConfirm() => CanConfirmOrder && IsNotBusy && _pendingAction is null && SelectedOrder is not null
        && (_pendingConfirmations.ContainsKey(SelectedOrder.Uid) || !HasPendingIntent && CanWrite && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.待确认,
    });
    private bool CanRetryPendingConfirmation() => CanConfirmOrder && IsNotBusy && _pendingAction is null
        && _pendingConfirmations.Count > 0;
    /// <summary>限流和授权等前置拒绝不查询原回执，不能据此丢弃已有的未决订单确认。</summary>
    private bool MustKeepConfirmationIntent(Guid commandId, string? code)
    {
        if (code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable)
            _unknownConfirmationIds.Add(commandId);
        var keep = _unknownConfirmationIds.Contains(commandId) && code is
            ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.RateLimited
            or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden or ServiceErrorCodes.ServiceUnavailable;
        if (!keep) _unknownConfirmationIds.Remove(commandId);
        return keep;
    }
    private bool CanCancelConfirmation() => CanCancelOrderConfirmation && CanWrite && CanBrowse && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.已确认,
    };
    private bool CanCloseOrder() => CanCloseOrderLifecycle && CanWrite && CanBrowse
        && SelectedOrder?.DocumentStatus == SalesOrderDocumentStatusEnum.Open;
    private bool CanDeleteDraft() => CanDeleteOrderDraft && CanWrite && CanBrowse && SelectedOrder is
    {
        DocumentStatus: SalesOrderDocumentStatusEnum.Open,
        ConfirmationStatus: ConfirmationStatusEnum.待确认,
    };
}
