using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Services.Caching;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购订单管理页模型；只负责订单查询、主从只读摘要和进入详情、预览等专页。</summary>
public sealed partial class PurchaseOrderManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-orders";
    private readonly IPurchaseOrderManagerApi _managerApi;
    private readonly IProcurementOrderApi _orderApi;
    private readonly IProcurementDialogService _procurementDialogs;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly ICurrentUserService _currentUser;
    private readonly ProcurementCommandSession _commandSession;
    private PurchaseOrderQuerySnapshot? _appliedQuery;
    private PurchaseOrderManagementRowModel[]? _visibleOrders;
    private int _sessionVersion;
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _synchronizingSelection;
    private bool _initialized;
    private readonly IReferenceDataCacheService? _referenceDataCache;

    public PurchaseOrderManagerViewModel(IPurchaseOrderManagerApi managerApi, IProcurementOrderApi orderApi,
        IProcurementDialogService procurementDialogs,
        INavigationService navigation, IDialogService dialogs,
        IApiCallRunner apiCallRunner, IMessageService messages,
        ICurrentUserService currentUser, ProcurementReversalSession? reversalSession = null,
        IProcurementReversalDialogService? reversalDialogs = null, IReferenceDataCacheService? referenceDataCache = null,
        ProcurementCommandSession? commandSession = null)
    {
        _managerApi = managerApi; _orderApi = orderApi; _procurementDialogs = procurementDialogs;
        _navigation = navigation; _dialogs = dialogs;
        _apiCallRunner = apiCallRunner; _messages = messages;
        _currentUser = currentUser; Title = "采购订单管理";
        _referenceDataCache = referenceDataCache;
        _commandSession = commandSession ?? new ProcurementCommandSession();
        _commandSession.Changed += OnCommandSessionChanged;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        _reversalSession = reversalSession ?? new ProcurementReversalSession(); _reversalDialogs = reversalDialogs;
        _reversalSession.Changed += OnReversalChanged;
        RegisterBusyAwareCommand(VoidOrderCommand); RegisterBusyAwareCommand(RetryReversalCommand);
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand); RegisterBusyAwareCommand(BatchConfirmCommand);
        RegisterBusyAwareCommand(OpenDetailCommand); RegisterBusyAwareCommand(CreateIndependentCommand);
        RegisterBusyAwareCommand(GetFromNetDemandCommand); RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(OpenFollowUpCommand);
        RegisterBusyAwareCommand(OpenTraceCommand);
        RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(RetryPendingCommand);
    }

    public ObservableCollection<PurchaseOrderManagementRowModel> Orders { get; } = [];
    public ObservableCollection<PurchaseOrderManagementLineModel> Lines { get; } = [];
    /// <summary>共享枚举直接作为筛选值，中文名称由页面转换器提供。</summary>
    public static IReadOnlyList<ConfirmationStatusEnum> ConfirmationStatuses { get; } = Array.AsReadOnly(Enum.GetValues<ConfirmationStatusEnum>());
    /// <summary>生命周期多选候选，避免在页面模型中重复维护中文描述。</summary>
    public static IReadOnlyList<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; } = Array.AsReadOnly(Enum.GetValues<PurchaseOrderLifecycleStatusEnum>());
    /// <summary>服务端计算的整单货好状态候选。</summary>
    public static IReadOnlyList<PurchaseReadyStatusEnum> ReadyStatuses { get; } = Array.AsReadOnly(Enum.GetValues<PurchaseReadyStatusEnum>());
    /// <summary>服务端计算的整单正式收货状态候选。</summary>
    public static IReadOnlyList<PurchaseReceiptStatusEnum> ReceiptStatuses { get; } = Array.AsReadOnly(Enum.GetValues<PurchaseReceiptStatusEnum>());
    /// <summary>采购行计划交期风险单选候选。</summary>
    public static IReadOnlyList<PurchaseDueViewEnum> DueViews { get; } = Array.AsReadOnly(Enum.GetValues<PurchaseDueViewEnum>());
    /// <summary>首次打开默认待确认；清空集合表示不限制确认状态。</summary>
    public ObservableCollection<ConfirmationStatusEnum> SelectedConfirmationStatuses { get; } = [ConfirmationStatusEnum.待确认];
    public ObservableCollection<PurchaseOrderLifecycleStatusEnum> SelectedLifecycleStatuses { get; } = [];
    public ObservableCollection<PurchaseReadyStatusEnum> SelectedReadyStatuses { get; } = [];
    public ObservableCollection<PurchaseReceiptStatusEnum> SelectedReceiptStatuses { get; } = [];
    [ObservableProperty] public partial PurchaseDueViewEnum SelectedDueView { get; set; } = PurchaseDueViewEnum.All;
    /// <summary>直接提供完整客户缓存，不按是否存在采购裁剪。</summary>
    public ReadOnlyObservableCollection<CustomerDto>? CustomerOptions => _referenceDataCache?.Customers;
    /// <summary>直接提供完整供应商缓存，不按当前筛选或采购历史裁剪。</summary>
    public ReadOnlyObservableCollection<SupplierDto>? SupplierOptions => _referenceDataCache?.Suppliers;
    /// <summary>查询客户多选，使用采购头保存的客户代码精确匹配。</summary>
    public ObservableCollection<CustomerDto> SelectedCustomers { get; } = [];
    /// <summary>查询供应商多选，使用稳定供应商身份。</summary>
    public ObservableCollection<SupplierDto> SelectedSuppliers { get; } = [];
    /// <summary>控制更多条件浮层，收起不丢失待应用输入。</summary>
    [ObservableProperty] public partial bool IsMoreFiltersOpen { get; set; }
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    /// <summary>获取当前加载页已勾选的采购单数量。</summary>
    public int SelectedCount => VisibleOrders.Count(static order => order.IsChecked);
    /// <summary>显示当前页选择范围，避免把勾选误解为全部服务端分页。</summary>
    public string SelectionSummary => $"当前可见已选 {SelectedCount} 张";
    /// <summary>获取当前用户的采购读取权限；服务端仍会对每次请求实时鉴权。</summary>
    public bool HasReadPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);
    /// <summary>获取当前用户的采购创建权限；服务端仍会对每次命令实时鉴权。</summary>
    public bool HasCreatePermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate);
    /// <summary>获取当前用户的采购确认权限；列表候选状态不能替代服务端最终资格判断。</summary>
    public bool HasConfirmPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm);
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? OrderDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? OrderDateTo { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateTo { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateTo { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelConfirmationCommand))]
    [NotifyCanExecuteChangedFor(nameof(VoidOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryReversalCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryPendingCommand))]
    public partial PurchaseOrderManagementRowModel? SelectedOrder { get; set; }
    [ObservableProperty] public partial PurchaseOrderManagementLineModel? SelectedLine { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载采购订单。";
    [ObservableProperty] public partial string DetailStatusText { get; set; } = "请选择采购订单查看行摘要。";
    /// <summary>当前页表头勾选状态；null 表示部分选择。</summary>
    [ObservableProperty]
    public partial bool? IsAllCurrentPageChecked { get; set; }

    public async Task InitializeAsync()
    {
        if (_initialized) return; _initialized = true;
        if (!HasReadPermission) { ResultSummary = "当前账号没有采购订单读取权限。"; return; }
        if (_referenceDataCache is not null)
        {
            var loaded = await _referenceDataCache.EnsureLoadedAsync();
            if (_reversalDisposed) return;
            if (!loaded.Success) _messages.ShowWarning("基础资料加载失败", loaded.Message);
        }
        await LoadPageAsync();
    }

    partial void OnSelectedOrderChanged(PurchaseOrderManagementRowModel? value) => _ = LoadDetailAsync(value);
    partial void OnIsAllCurrentPageCheckedChanged(bool? value)
    {
        if (_synchronizingSelection || value is null || IsBusy) return;
        _synchronizingSelection = true;
        try
        {
            foreach (var order in VisibleOrders) order.IsChecked = value.Value;
        }
        finally
        {
            _synchronizingSelection = false;
        }
        RefreshSelectionState();
    }

    [RelayCommand(CanExecute = nameof(CanQuery))] private async Task SearchAsync() { if (!CanQuery()) return; await LoadPageAsync(CaptureQuery(), 1, PageSize); }
    [RelayCommand(CanExecute = nameof(CanQuery))] private Task RefreshAsync() => CanQuery() ? LoadPageAsync() : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task ClearFiltersAsync()
    {
        Keyword = string.Empty;
        SelectedCustomers.Clear(); SelectedSuppliers.Clear();
        OrderDateFrom = OrderDateTo = null;
        SelectedConfirmationStatuses.Clear();
        SelectedLifecycleStatuses.Clear();
        SelectedReadyStatuses.Clear(); SelectedReceiptStatuses.Clear(); SelectedDueView = PurchaseDueViewEnum.All;
        PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null;
        return LoadPageAsync(CaptureQuery(), 1, PageSize);
    }
    [RelayCommand(CanExecute = nameof(CanQuery))] private Task PageChangeAsync(PageChangeRequest? request) => !CanQuery() || request is null ? Task.CompletedTask : LoadPageAsync(_appliedQuery, request.NewIndex, request.NewSize);

    /// <summary>应用更多条件走同一查询接纳边界。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task ApplyMoreFiltersAsync() { IsMoreFiltersOpen = false; await SearchAsync(); }
    /// <summary>仅清除更多日期输入，点击应用后才改变当前事实。</summary>
    [RelayCommand] private void ClearMoreFilters() => OrderDateFrom = OrderDateTo = PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null;
    /// <summary>收起浮层保留输入。</summary>
    [RelayCommand] private void CollapseMoreFilters() => IsMoreFiltersOpen = false;
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedOrder!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedOrder.PurchaseOrderId });
    /// <summary>单条确认在一个页面 Busy 内预检和提交，冻结负载后与详情共享未决状态。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmOrder))]
    private async Task ConfirmAsync()
    {
        if (!CanConfirmOrder()) return;
        var order = SelectedOrder!; var user = ReversalUser;
        await RunWriteAsync(async () =>
        {
            var context = await _apiCallRunner.RunAsync(() => _orderApi.GetConfirmationContextAsync(order.PurchaseOrderId), "读取确认依据失败");
            if (_reversalDisposed || user != ReversalUser) return;
            if (!context.Success || context.Data is null) { await HandleCommandFailureAsync(context, "读取确认依据失败", order.PurchaseOrderId); return; }
            if (!await _dialogs.ConfirmAsync("确认采购订单", $"采购单：{order.OrderNo}\n供应商：{order.SupplierName}\n行数：{context.Data.Lines.Count}\n确认后锁定采购并允许入库，是否继续？", "确认")) return;
            if (_reversalDisposed || user != ReversalUser || !HasConfirmPermission || HasPendingConfirmation(order.PurchaseOrderId)) return;
            var command = new ConfirmProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = context.Data.Version, ContextFingerprint = context.Data.Fingerprint };
            if (!_commandSession.Freeze(user, order.PurchaseOrderId, ProcurementCommandKind.Confirm, command)) return;
            var result = await _commandSession.SendAsync(user, order.PurchaseOrderId, _orderApi, _apiCallRunner);
            if (_reversalDisposed || user != ReversalUser) return;
            if (_commandSession.Find(user, order.PurchaseOrderId) is not null)
            {
                order.MarkConfirmationResultUnknown(); ResultSummary = result.Message;
                _messages.ShowWarning("确认结果待核实", ResultSummary); return;
            }
            if (!result.Success || result.Data is null) { await HandleCommandFailureAsync(result, "确认采购失败", order.PurchaseOrderId); return; }
            var refreshed = await LoadPageCoreAsync(order.PurchaseOrderId);
            if (!refreshed) ResultSummary = "采购已确认，列表刷新失败，请刷新核实；不要重复提交。";
            _messages.ShowSuccess("采购已确认", order.OrderNo);
        }, "正在确认采购订单...");
    }
    /// <summary>批量确认当前页勾选订单；预检、一次确认、顺序写入和最终刷新共用一个 Busy 所有权。</summary>
    [RelayCommand(CanExecute = nameof(CanBatchConfirm))]
    private async Task BatchConfirmAsync()
    {
        if (!CanBatchConfirm()) return;
        await RunWriteAsync(async () =>
        {
            var user = ReversalUser;
            var snapshot = VisibleOrders.Where(static order => order.IsChecked).ToArray();
            var plans = new List<BatchConfirmationPlan>(snapshot.Length);
            var skipped = new List<string>();
            var succeeded = new List<string>();
            var succeededIds = new HashSet<Guid>();
            var failed = new List<string>();
            var unknown = new List<string>();
            var notExecuted = new List<string>();
            foreach (var order in snapshot)
            {
                if (HasPendingConfirmation(order.PurchaseOrderId))
                {
                    skipped.Add($"{order.OrderNo}：上次确认结果待核实，未生成新命令。");
                    continue;
                }
                if (!order.CanConfirm)
                {
                    var reason = string.IsNullOrWhiteSpace(order.CommandBlockedReason)
                        ? "当前状态不可确认。"
                        : order.CommandBlockedReason;
                    skipped.Add($"{order.OrderNo}：{reason}");
                    continue;
                }

                SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementConfirmationContextDto> context;
                try
                {
                    context = await _apiCallRunner.RunAsync(
                        () => _orderApi.GetConfirmationContextAsync(order.PurchaseOrderId),
                        "读取采购确认依据失败");
                }
                catch
                {
                    skipped.Add($"{order.OrderNo}：读取确认依据发生未识别错误。");
                    continue;
                }
                if (!context.Success || context.Data is null)
                {
                    skipped.Add($"{order.OrderNo}：{GetFailureText(context, "读取确认依据失败。")}");
                    continue;
                }

                plans.Add(new BatchConfirmationPlan
                {
                    Order = order,
                    Command = new ConfirmProcurementOrderCommand
                    {
                        CommandId = Guid.NewGuid(),
                        ExpectedVersion = context.Data.Version,
                        ContextFingerprint = context.Data.Fingerprint,
                    }
                });
            }

            if (plans.Count == 0)
            {
                var skippedDetails = BuildBatchSummary(succeeded, skipped, failed, unknown, notExecuted);
                ResultSummary = BuildBatchCountSummary(succeeded, skipped, failed, unknown, notExecuted);
                await _dialogs.ShowContentAsync("批量确认未执行", skippedDetails);
                return;
            }

            var prompt = BuildBatchConfirmationPrompt(snapshot.Length, plans, skipped);
            if (!await _dialogs.ConfirmAsync("批量确认采购订单", prompt, "确认可执行订单"))
            {
                ResultSummary = $"已取消批量确认，当前页已选 {snapshot.Length} 张采购单均未提交。";
                return;
            }

            var stopped = false;
            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                if (_reversalDisposed || user != ReversalUser || !HasConfirmPermission) break;
                if (!_commandSession.Freeze(user, plan.Order.PurchaseOrderId, ProcurementCommandKind.Confirm, plan.Command))
                { skipped.Add($"{plan.Order.OrderNo}：已有未决写入，未提交新命令。"); continue; }
                SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementOrderCommandResult>? result = null;
                try
                {
                    result = await _commandSession.SendAsync(user, plan.Order.PurchaseOrderId, _orderApi, _apiCallRunner);
                }
                catch
                {
                    // 未识别异常发生在写请求边界，无法证明服务端未执行，必须按未知结果停止后续提交。
                }

                if (result?.Success == true && result.Data is not null)
                {
                    succeeded.Add(plan.Order.OrderNo);
                    succeededIds.Add(plan.Order.PurchaseOrderId);
                    plan.Order.IsChecked = false;
                    continue;
                }
                if (_commandSession.Find(user, plan.Order.PurchaseOrderId) is null && !string.IsNullOrWhiteSpace(result?.Error?.Code))
                {
                    failed.Add($"{plan.Order.OrderNo}：{GetFailureText(result, "确认失败。")}");
                    continue;
                }

                unknown.Add($"{plan.Order.OrderNo}：确认结果待核实，已停止后续提交；请刷新查看权威状态，仍待确认时继续阻止重发。");
                TrackPendingConfirmation(plan.Order, plan.Command);
                for (var remaining = index + 1; remaining < plans.Count; remaining++)
                    notExecuted.Add($"{plans[remaining].Order.OrderNo}：前序订单结果未知，未执行。");
                stopped = true;
                break;
            }

            var preserveCheckedIds = snapshot
                .Where(order => !succeededIds.Contains(order.PurchaseOrderId))
                .Select(static order => order.PurchaseOrderId)
                .ToHashSet();
            var selectedId = SelectedOrder?.PurchaseOrderId;
            var detail = BuildBatchSummary(succeeded, skipped, failed, unknown, notExecuted);
            var summary = BuildBatchCountSummary(succeeded, skipped, failed, unknown, notExecuted);
            var refreshSucceeded = false;
            try
            {
                refreshSucceeded = await LoadPageCoreAsync(selectedId, preserveCheckedIds);
            }
            catch
            {
                // 批量执行结果已经形成；刷新异常只作为后续核实提示，不能覆盖逐单结果。
            }
            RefreshSelectionState();
            ResultSummary = refreshSucceeded
                ? summary
                : $"{summary} 服务端状态刷新失败，逐单结果请以结果弹窗为准。";
            var dialogDetail = refreshSucceeded
                ? detail
                : $"{detail}\n\n服务端状态刷新失败，请稍后核实；上述逐单执行结果仍保留。";
            await _dialogs.ShowContentAsync(stopped ? "批量确认已停止" : "批量确认结果", dialogDetail);
        }, "正在批量确认采购订单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelOrderConfirmation))]
    private Task CancelConfirmationAsync() => StartReversalAsync(ProcurementReversalOperation.CancelConfirmation);
    [RelayCommand(CanExecute = nameof(CanCreateIndependent))]
    private Task CreateIndependentAsync() => _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{Guid.NewGuid():N}", new PurchaseOrderDocumentOpenRequest());
    [RelayCommand] private Task OpenFollowUpAsync() => _navigation.OpenAsync(PurchaseOrderProgressWorkspaceViewModel.RouteKey);
    /// <summary>以采购单稳定身份打开只读追溯页签，重复打开激活同一页签。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenTraceAsync() => _navigation.OpenDocumentAsync(PurchaseOrderTraceViewModel.RouteKey,
        $"purchase-order-trace:{SelectedOrder!.PurchaseOrderId:N}",
        new PurchaseOrderTraceOpenRequest { PurchaseOrderId = SelectedOrder.PurchaseOrderId, PurchaseOrderLineId = SelectedLine?.PurchaseOrderLineId });
    [RelayCommand(CanExecute = nameof(CanCreateIndependent))]
    private async Task GetFromNetDemandAsync()
    {
        var result = await _procurementDialogs.ShowFromMaterialAsync();
        if (result.HasAppliedResults) await LoadPageAsync();
    }

    private async Task LoadPageAsync(PurchaseOrderQuerySnapshot? query = null, int? page = null, int? size = null)
    {
        if (!HasReadPermission) { ResultSummary = "当前账号没有采购订单读取权限。"; return; }
        ValidationSummary = null;
        await RunBusyAsync(async () => { await LoadPageCoreAsync(query: query, page: page, size: size); }, "正在加载采购订单...", false);
        if (!string.IsNullOrWhiteSpace(ValidationSummary))
            ResultSummary = $"采购订单加载失败：{ValidationSummary}";
    }

    /// <summary>执行可替代的管理分页查询；调用方负责最外层 Busy，并可指定刷新后应恢复的订单。</summary>
    private async Task<bool> LoadPageCoreAsync(
        Guid? preservePurchaseOrderId = null,
        IReadOnlySet<Guid>? preserveCheckedIds = null,
        PurchaseOrderQuerySnapshot? query = null, int? page = null, int? size = null)
    {
        if (_reversalDisposed || !HasReadPermission) return false;
        query ??= _appliedQuery ?? CaptureQuery();
        var request = query.ForPage(page ?? PageIndex, size ?? PageSize);
        if (request.OrderDateFrom > request.OrderDateTo || request.PlannedDeliveryDateFrom > request.PlannedDeliveryDateTo
            || request.ExpectedCompletionDateFrom > request.ExpectedCompletionDateTo)
        { ResultSummary = "日期起点不能晚于终点。"; return false; }
        _queryCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _queryCancellation = cts;
        var session = _sessionVersion;
        try
        {
            var selectedId = preservePurchaseOrderId ?? SelectedOrder?.PurchaseOrderId;
            var result = await _apiCallRunner.RunAsync(
                () => _managerApi.SearchAsync(request, cts.Token),
                cts.Token,
                "加载采购订单失败");

            if (_reversalDisposed || session != _sessionVersion || cts.IsCancellationRequested || !ReferenceEquals(_queryCancellation, cts)) return false;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = GetFailureText(result, "采购订单加载失败。");
                return false;
            }

            DetachOrderEvents();
            _visibleOrders = null;
            Orders.Clear();
            foreach (var item in result.Data.Items)
            {
                var order = item.Adapt<PurchaseOrderManagementRowModel>();
                order.IsChecked = preserveCheckedIds?.Contains(order.PurchaseOrderId) == true;
                ApplyPendingConfirmationState(item, order);
                order.PropertyChanged += OnOrderPropertyChanged;
                Orders.Add(order);
            }
            _appliedQuery = query;
            TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : request.PageIndex;
            PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : request.PageSize;
            SelectedOrder = Orders.FirstOrDefault(x => x.PurchaseOrderId == selectedId) ?? Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张采购订单，本页 {Orders.Count} 张。";
            RefreshSelectionState();
            return true;
        }
        finally
        {
            if (ReferenceEquals(_queryCancellation, cts))
                _queryCancellation = null;
            cts.Dispose();
        }
    }

    /// <summary>切换主表选择时取消旧请求，只接纳当前采购单的行摘要。</summary>
    private async Task LoadDetailAsync(PurchaseOrderManagementRowModel? order)
    {
        _detailCancellation?.Cancel(); var cts = new CancellationTokenSource(); _detailCancellation = cts;
        var session = _sessionVersion;
        try
        {
            Lines.Clear(); SelectedLine = null;
            if (order is null) { DetailStatusText = "请选择采购订单查看行摘要。"; return; }
            DetailStatusText = "正在加载采购行...";
            var result = await _apiCallRunner.RunAsync(() => _managerApi.GetLinesAsync(order.PurchaseOrderId, cts.Token), cts.Token, "采购行加载失败");
            if (_reversalDisposed || session != _sessionVersion || cts.IsCancellationRequested
                || !ReferenceEquals(_detailCancellation, cts) || SelectedOrder?.PurchaseOrderId != order.PurchaseOrderId) return;
            if (result.Success && result.Data is not null)
            { foreach (var line in result.Data) Lines.Add(line.Adapt<PurchaseOrderManagementLineModel>()); SelectedLine = Lines.FirstOrDefault(); DetailStatusText = $"共 {Lines.Count} 条采购行，整单全量加载；表头只筛选当前数据。"; }
            else DetailStatusText = result.Error?.Message ?? result.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch { if (!_reversalDisposed && ReferenceEquals(_detailCancellation, cts)) DetailStatusText = "采购行加载失败，请刷新重试。"; }
        finally { if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null; cts.Dispose(); }
    }

    /// <summary>普通查询捕获输入，后续页码及刷新只复制这份已应用条件。</summary>
    private PurchaseOrderQuerySnapshot CaptureQuery() => new(new PurchaseOrderManagerSearchRequest
    {
        Keyword = Keyword.Trim(), CustomerCodes = SelectedCustomers.Select(x => x.CustomerCode).Distinct().ToList(),
        SupplierIds = SelectedSuppliers.Select(x => x.Uid).Distinct().ToList(),
        OrderDateFrom = OrderDateFrom, OrderDateTo = OrderDateTo,
        ConfirmationStatuses = SelectedConfirmationStatuses.ToList(), LifecycleStatuses = SelectedLifecycleStatuses.ToList(),
        ReadyStatuses = SelectedReadyStatuses.ToList(), ReceiptStatuses = SelectedReceiptStatuses.ToList(), DueView = SelectedDueView,
        PlannedDeliveryDateFrom = PlannedDeliveryDateFrom, PlannedDeliveryDateTo = PlannedDeliveryDateTo,
        ExpectedCompletionDateFrom = ExpectedCompletionDateFrom, ExpectedCompletionDateTo = ExpectedCompletionDateTo
    });

    private bool CanOpenDetail() => HasReadPermission && IsNotBusy && SelectedOrder is not null;
    private bool CanQuery() => !_reversalDisposed && HasReadPermission && IsNotBusy;
    private bool CanBatchConfirm() => HasReadPermission && HasConfirmPermission && IsNotBusy
        && VisibleOrders.Any(order => order.IsChecked && !HasPendingConfirmation(order.PurchaseOrderId));
    private bool CanCreateIndependent() => HasCreatePermission && IsNotBusy;
    private bool CanConfirmOrder() => HasReadPermission && HasConfirmPermission && IsNotBusy
        && SelectedOrder?.CanConfirm == true
        && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    private bool CanCancelOrderConfirmation() => HasConfirmPermission && IsNotBusy
        && SelectedOrder is { LifecycleStatus: PurchaseOrderLifecycleStatusEnum.Active, ConfirmationStatus: ConfirmationStatusEnum.已确认 }
        && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);

    /// <summary>同步当前页表头三态、已选数量和批量命令可执行状态。</summary>
    private void RefreshSelectionState()
    {
        var visibleCount = VisibleOrders.Count();
        var selectedCount = SelectedCount;
        _synchronizingSelection = true;
        try
        {
            IsAllCurrentPageChecked = visibleCount == 0 || selectedCount == 0
                ? false
                : selectedCount == visibleCount ? true : null;
        }
        finally
        {
            _synchronizingSelection = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        BatchConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>监听行勾选变化，使表头三态和批量命令立即响应。</summary>
    private void OnOrderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchaseOrderManagementRowModel.IsChecked) && !_synchronizingSelection)
            RefreshSelectionState();
        if (e.PropertyName == nameof(PurchaseOrderManagementRowModel.HasUnknownConfirmationResult)
            && ReferenceEquals(sender, SelectedOrder))
            ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>移除旧页行事件，避免替换分页结果后残留订阅。</summary>
    private void DetachOrderEvents()
    {
        foreach (var order in Orders)
            order.PropertyChanged -= OnOrderPropertyChanged;
    }

    /// <summary>提取服务返回的可读失败原因，并为缺失信息提供稳定兜底。</summary>
    private static string GetFailureText<T>(SwiftExport.Contracts.Wrappers.ServiceResult<T> result, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(result.Error?.Message)) return result.Error.Message;
        return string.IsNullOrWhiteSpace(result.Message) ? fallback : result.Message;
    }

    /// <summary>生成批量确认前的单次汇总提示，明确可执行与跳过范围。</summary>
    private static string BuildBatchConfirmationPrompt(
        int selectedCount,
        IReadOnlyCollection<BatchConfirmationPlan> plans,
        IReadOnlyCollection<string> skipped)
    {
        var text = $"当前页共勾选 {selectedCount} 张采购单。\n可执行：{plans.Count} 张；跳过：{skipped.Count} 张。";
        if (plans.Count > 0)
            text += $"\n\n实际提交：\n{string.Join("\n", plans.Select(static plan => $"{plan.Order.OrderNo} · {plan.Order.SupplierName}"))}";
        if (skipped.Count > 0)
            text += $"\n\n跳过明细：\n{string.Join("\n", skipped)}";
        return $"{text}\n\n确认后将按列表顺序逐张提交可执行订单，是否继续？";
    }

    /// <summary>生成适合列表底部单行展示的批量结果计数。</summary>
    private static string BuildBatchCountSummary(
        IReadOnlyCollection<string> succeeded,
        IReadOnlyCollection<string> skipped,
        IReadOnlyCollection<string> failed,
        IReadOnlyCollection<string> unknown,
        IReadOnlyCollection<string> notExecuted) =>
        $"批量确认：成功 {succeeded.Count}，跳过 {skipped.Count}，失败 {failed.Count}，未知 {unknown.Count}，未执行 {notExecuted.Count}。";

    /// <summary>记录当前管理页会话内的未决确认身份，并立即关闭单条和批量重发入口。</summary>
    private void TrackPendingConfirmation(PurchaseOrderManagementRowModel order, ConfirmProcurementOrderCommand command)
    {
        order.MarkConfirmationResultUnknown();
        BatchConfirmCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>查询当前订单是否存在尚未由权威已确认状态消解的写入结果。</summary>
    private bool HasPendingConfirmation(Guid purchaseOrderId) => _commandSession.Find(ReversalUser, purchaseOrderId) is not null
        || _reversalSession.Find(ReversalUser, purchaseOrderId) is not null;

    /// <summary>允许在权限和身份匹配时显式重试原请求，不重新读取上下文或生成命令。</summary>
    private bool CanRetryPending() => !_reversalDisposed && IsNotBusy && SelectedOrder is not null
        && !_commandSession.IsSending(ReversalUser, SelectedOrder.PurchaseOrderId)
        && _commandSession.Find(ReversalUser, SelectedOrder.PurchaseOrderId) is { } pending
        && (pending.Kind == ProcurementCommandKind.Confirm ? HasConfirmPermission : _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit));

    [RelayCommand(CanExecute = nameof(CanRetryPending))]
    private async Task RetryPendingAsync()
    {
        if (!CanRetryPending()) return;
        var uid = SelectedOrder!.PurchaseOrderId; var user = ReversalUser;
        await RunWriteAsync(async () =>
        {
            var result = await _commandSession.SendAsync(user, uid, _orderApi, _apiCallRunner);
            if (_reversalDisposed || user != ReversalUser) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message; _messages.ShowWarning("原采购命令结果", result.Message); return; }
            if (!await LoadPageCoreAsync(uid)) ResultSummary = "原采购命令已成功，列表刷新失败，请刷新核实。";
            _messages.ShowSuccess("原采购命令已确认", result.Data.OrderNo);
        }, "正在重试原采购命令...");
    }

    private IEnumerable<PurchaseOrderManagementRowModel> VisibleOrders => _visibleOrders ?? Orders.AsEnumerable();
    /// <summary>由视图提交当前可见模型，全选及批量确认不会把隐藏行加入操作。</summary>
    public void UpdateVisibleOrders(IEnumerable<PurchaseOrderManagementRowModel> orders)
    { _visibleOrders = orders.Where(Orders.Contains).ToArray(); RefreshSelectionState(); }

    /// <summary>投影跨页面未决状态，其他页面完成后须刷新旧行才能再次操作。</summary>
    private void OnCommandSessionChanged(object? sender, EventArgs e)
    {
        if (_reversalDisposed) return;
        foreach (var order in Orders)
        {
            if (_commandSession.Find(ReversalUser, order.PurchaseOrderId) is not null) order.MarkConfirmationResultUnknown();
            else
            {
                if (order.HasUnknownConfirmationResult && IsNotBusy)
                { order.CanConfirm = false; order.CommandBlockedReason = "原写入已由其他页面处理，请刷新核实状态。"; }
                order.ClearConfirmationResultUnknown();
            }
        }
        OnReversalChanged(sender, e); RetryPendingCommand.NotifyCanExecuteChanged();
    }

    /// <summary>会话变化取消读取并清除旧事实，不跨账号复用未决命令或筛选结果。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        _sessionVersion++; _queryCancellation?.Cancel(); _detailCancellation?.Cancel();
        DetachOrderEvents(); Orders.Clear(); Lines.Clear(); SelectedOrder = null; SelectedLine = null;
        _appliedQuery = null; _visibleOrders = null; TotalCount = 0;
        OnCommandSessionChanged(sender, e); RefreshSelectionState();
    }

    /// <summary>把会话未决状态投影到重建行；仅权威查询明确返回已确认时解除门禁。</summary>
    private void ApplyPendingConfirmationState(
        PurchaseOrderManagerListItemDto source,
        PurchaseOrderManagementRowModel target)
    {
        _commandSession.ObserveConfirmation(ReversalUser, source.PurchaseOrderId, source.ConfirmationStatus);
        if (_commandSession.Find(ReversalUser, source.PurchaseOrderId) is not null) target.MarkConfirmationResultUnknown();
    }

    /// <summary>生成批量执行结果，保留成功、跳过、明确失败、未知及未执行的边界。</summary>
    private static string BuildBatchSummary(
        IReadOnlyCollection<string> succeeded,
        IReadOnlyCollection<string> skipped,
        IReadOnlyCollection<string> failed,
        IReadOnlyCollection<string> unknown,
        IReadOnlyCollection<string> notExecuted)
    {
        var sections = new List<string>
        {
            $"成功 {succeeded.Count} 张，跳过 {skipped.Count} 张，明确失败 {failed.Count} 张，结果未知 {unknown.Count} 张，未执行 {notExecuted.Count} 张。"
        };
        AddSection(sections, "成功", succeeded);
        AddSection(sections, "跳过", skipped);
        AddSection(sections, "明确失败", failed);
        AddSection(sections, "结果未知", unknown);
        AddSection(sections, "未执行", notExecuted);
        return string.Join("\n\n", sections);
    }

    /// <summary>仅在存在内容时追加结果分组，避免空标题干扰阅读。</summary>
    private static void AddSection(List<string> sections, string title, IReadOnlyCollection<string> items)
    {
        if (items.Count > 0) sections.Add($"{title}：\n{string.Join("\n", items)}");
    }

    /// <summary>按稳定错误码处理命令失败；并发或确认依据失效时刷新并尽量恢复原选择。</summary>
    private async Task HandleCommandFailureAsync<T>(
        SwiftExport.Contracts.Wrappers.ServiceResult<T> result,
        string title,
        Guid purchaseOrderId)
    {
        var message = result.Error?.Message ?? result.Message;
        var code = result.Error?.Code;
        if (code == SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict
            || code == ProcurementErrorCodes.BomBasisChanged)
        {
            await LoadPageCoreAsync(purchaseOrderId);
            _messages.ShowWarning(code == ProcurementErrorCodes.BomBasisChanged ? "确认依据已变化" : "采购数据已变化", message);
            return;
        }

        _messages.ShowError(title, message);
    }
    /// <summary>保存预检阶段生成的固定命令身份和依据，写入阶段不得重新生成。</summary>
    private sealed class BatchConfirmationPlan
    {
        public required PurchaseOrderManagementRowModel Order { get; init; }
        public required ConfirmProcurementOrderCommand Command { get; init; }
    }

    public void Dispose() { _reversalDisposed = true; _reversalSession.Changed -= OnReversalChanged; _commandSession.Changed -= OnCommandSessionChanged; _currentUser.CurrentUserChanged -= OnCurrentUserChanged; DetachOrderEvents(); _queryCancellation?.Cancel(); _detailCancellation?.Cancel(); }
}
