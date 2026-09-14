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
    private readonly Dictionary<Guid, PendingConfirmationState> _pendingConfirmations = [];
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _synchronizingSelection;
    private bool _initialized;

    public PurchaseOrderManagerViewModel(IPurchaseOrderManagerApi managerApi, IProcurementOrderApi orderApi,
        IProcurementDialogService procurementDialogs,
        INavigationService navigation, IDialogService dialogs,
        IApiCallRunner apiCallRunner, IMessageService messages,
        ICurrentUserService currentUser)
    {
        _managerApi = managerApi; _orderApi = orderApi; _procurementDialogs = procurementDialogs;
        _navigation = navigation; _dialogs = dialogs;
        _apiCallRunner = apiCallRunner; _messages = messages;
        _currentUser = currentUser; Title = "采购订单管理";
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand); RegisterBusyAwareCommand(BatchConfirmCommand);
        RegisterBusyAwareCommand(OpenDetailCommand); RegisterBusyAwareCommand(CreateIndependentCommand);
        RegisterBusyAwareCommand(GetFromNetDemandCommand); RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(OpenTraceCommand); RegisterBusyAwareCommand(OpenFollowUpCommand);
        RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
    }

    public ObservableCollection<PurchaseOrderManagementRowModel> Orders { get; } = [];
    public ObservableCollection<PurchaseOrderManagementLineModel> Lines { get; } = [];
    public IReadOnlyList<PurchaseOrderFilterOption<ConfirmationStatusEnum>> ConfirmationOptions { get; } =
    [
        new(ConfirmationStatusEnum.待确认, "待确认"),
        new(ConfirmationStatusEnum.已确认, "已确认")
    ];
    public IReadOnlyList<PurchaseOrderFilterOption<PurchaseOrderLifecycleStatusEnum>> LifecycleOptions { get; } =
    [
        new(PurchaseOrderLifecycleStatusEnum.Active, "活动"),
        new(PurchaseOrderLifecycleStatusEnum.Closed, "已关闭"),
        new(PurchaseOrderLifecycleStatusEnum.Voided, "已作废")
    ];
    public ObservableCollection<PurchaseOrderFilterOption<ConfirmationStatusEnum>> SelectedConfirmationStatuses { get; } = [];
    public ObservableCollection<PurchaseOrderFilterOption<PurchaseOrderLifecycleStatusEnum>> SelectedLifecycleStatuses { get; } = [];
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    /// <summary>获取当前加载页已勾选的采购单数量。</summary>
    public int SelectedCount => Orders.Count(static order => order.IsChecked);
    /// <summary>显示当前页选择范围，避免把勾选误解为全部服务端分页。</summary>
    public string SelectionSummary => $"当前页已选 {SelectedCount} 张";
    /// <summary>获取当前用户的采购读取权限；服务端仍会对每次请求实时鉴权。</summary>
    public bool HasReadPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);
    /// <summary>获取当前用户的采购创建权限；服务端仍会对每次命令实时鉴权。</summary>
    public bool HasCreatePermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate);
    /// <summary>获取当前用户的采购确认权限；列表候选状态不能替代服务端最终资格判断。</summary>
    public bool HasConfirmPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm);
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierKeyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateTo { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateTo { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelConfirmationCommand))]
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
        await LoadPageAsync();
    }

    partial void OnSelectedOrderChanged(PurchaseOrderManagementRowModel? value) => _ = LoadDetailAsync(value);
    partial void OnIsAllCurrentPageCheckedChanged(bool? value)
    {
        if (_synchronizingSelection || value is null || IsBusy) return;
        _synchronizingSelection = true;
        try
        {
            foreach (var order in Orders) order.IsChecked = value.Value;
        }
        finally
        {
            _synchronizingSelection = false;
        }
        RefreshSelectionState();
    }

    [RelayCommand(CanExecute = nameof(CanQuery))] private async Task SearchAsync() { if (!CanQuery()) return; PageIndex = 1; await LoadPageAsync(); }
    [RelayCommand(CanExecute = nameof(CanQuery))] private Task RefreshAsync() => CanQuery() ? LoadPageAsync() : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task ClearFiltersAsync()
    {
        Keyword = OrderNo = SourceNo = SupplierKeyword = Sku = string.Empty;
        SelectedConfirmationStatuses.Clear();
        SelectedLifecycleStatuses.Clear();
        PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null;
        PageIndex = 1;
        return LoadPageAsync();
    }
    [RelayCommand(CanExecute = nameof(CanQuery))] private Task PageChangeAsync(PageChangeRequest? request) { if (!CanQuery() || request is null) return Task.CompletedTask; PageIndex = request.NewIndex; PageSize = request.NewSize; return LoadPageAsync(); }
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedOrder!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedOrder.PurchaseOrderId });
    [RelayCommand(CanExecute = nameof(CanConfirmOrder))]
    private async Task ConfirmAsync()
    {
        if (!CanConfirmOrder()) return;
        var order = SelectedOrder!;
        var context = await _orderApi.GetConfirmationContextAsync(order.PurchaseOrderId);
        if (!context.Success || context.Data is null)
        {
            await HandleCommandFailureAsync(context, "读取确认依据失败", order.PurchaseOrderId);
            return;
        }

        var requiresConsumptionBinding = context.Data.Lines
            .SelectMany(static line => line.Bom.Nodes)
            .Any(static node => node.IsCustomerSupplied);
        var prompt = $"采购单：{order.OrderNo}\n供应商：{order.SupplierName}\n行数：{context.Data.Lines.Count}\n\n"
            + (requiresConsumptionBinding
                ? "订单存在尚需选择的 BOM/客供件消耗绑定，将进入采购详情完成确认。"
                : "确认后将冻结当前 BOM 与消耗依据，是否继续？");
        if (!await _dialogs.ConfirmAsync("确认采购订单", prompt, requiresConsumptionBinding ? "进入详情" : "确认"))
            return;

        if (requiresConsumptionBinding)
        {
            await OpenDetailAsync();
            return;
        }

        await RunWriteAsync(async () =>
        {
            if (HasPendingConfirmation(order.PurchaseOrderId))
            {
                ResultSummary = $"{order.OrderNo} 仍有待核实的确认请求，当前会话不会生成新的确认命令。";
                _messages.ShowWarning("确认结果待核实", ResultSummary);
                return;
            }
            var command = new ConfirmProcurementOrderCommand
            {
                CommandId = Guid.NewGuid(),
                ExpectedVersion = context.Data.Version,
                ContextFingerprint = context.Data.Fingerprint,
                ConsumptionBindings = []
            };
            SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementOrderCommandResult>? result = null;
            try
            {
                result = await _apiCallRunner.RunAsync(
                    () => _orderApi.ConfirmAsync(order.PurchaseOrderId, command),
                    "确认采购订单失败");
            }
            catch
            {
                // 未识别异常发生在写请求边界时无法证明服务端未执行，必须保留原命令身份并关闭重发入口。
            }
            if (result?.Success != true || result.Data is null)
            {
                if (string.IsNullOrWhiteSpace(result?.Error?.Code))
                {
                    TrackPendingConfirmation(order, command);
                    ResultSummary = $"{order.OrderNo} 确认结果待核实；请刷新查看权威状态，仅明确已确认时解除当前会话门禁。";
                    _messages.ShowWarning("确认结果待核实", ResultSummary);
                    return;
                }
                await HandleCommandFailureAsync(result!, "确认采购失败", order.PurchaseOrderId);
                return;
            }

            await LoadPageCoreAsync(order.PurchaseOrderId);
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
            var snapshot = Orders.Where(static order => order.IsChecked).ToArray();
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
                if (RequiresConsumptionBinding(context.Data))
                {
                    skipped.Add($"{order.OrderNo}：需要进入采购详情选择 BOM/客供件消耗绑定。");
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
                        ConsumptionBindings = []
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
                SwiftExport.Contracts.Wrappers.ServiceResult<ProcurementOrderCommandResult>? result = null;
                try
                {
                    result = await _apiCallRunner.RunAsync(
                        () => _orderApi.ConfirmAsync(plan.Order.PurchaseOrderId, plan.Command),
                        "确认采购订单失败");
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
                if (!string.IsNullOrWhiteSpace(result?.Error?.Code))
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
    private void CancelConfirmation()
    {
        if (SelectedOrder is null) return;
        _messages.ShowWarning(
            "取消采购确认暂未开放",
            "该能力等待入库与库存反向流程完成后重建；当前不会发送取消请求，也不会修改采购状态。");
    }
    [RelayCommand(CanExecute = nameof(CanCreateIndependent))]
    private Task CreateIndependentAsync() => _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{Guid.NewGuid():N}", new PurchaseOrderDocumentOpenRequest());
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private async Task OpenTraceAsync()
    {
        var order = SelectedOrder!;
        var result = order.ConfirmationStatus == ConfirmationStatusEnum.已确认
            ? await _orderApi.GetConfirmationSnapshotAsync(order.PurchaseOrderId)
            : null;
        var text = result?.Success == true && result.Data is not null
            ? $"确认批次：{result.Data.ConfirmationId}\n冻结行数：{result.Data.Lines.Count}\n新链追溯当前只展示订单详情与冻结 BOM/消耗快照。"
            : "订单尚未形成确认快照。新链不伪造需求 Link、跟进或入库追溯数据。";
        await _dialogs.ShowContentAsync($"采购追溯 · {order.OrderNo}", text);
    }
    [RelayCommand] private Task OpenFollowUpAsync() => _navigation.OpenAsync(PurchaseOrderProgressWorkspaceViewModel.RouteKey);
    [RelayCommand(CanExecute = nameof(CanCreateIndependent))]
    private async Task GetFromNetDemandAsync()
    {
        var result = await _procurementDialogs.ShowFromMaterialAsync();
        if (result.HasAppliedResults) await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (!HasReadPermission) { ResultSummary = "当前账号没有采购订单读取权限。"; return; }
        ValidationSummary = null;
        await RunBusyAsync(async () => { await LoadPageCoreAsync(); }, "正在加载采购订单...", false);
        if (!string.IsNullOrWhiteSpace(ValidationSummary))
            ResultSummary = $"采购订单加载失败：{ValidationSummary}";
    }

    /// <summary>执行可替代的管理分页查询；调用方负责最外层 Busy，并可指定刷新后应恢复的订单。</summary>
    private async Task<bool> LoadPageCoreAsync(
        Guid? preservePurchaseOrderId = null,
        IReadOnlySet<Guid>? preserveCheckedIds = null)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _queryCancellation = cts;
        try
        {
            var selectedId = preservePurchaseOrderId ?? SelectedOrder?.PurchaseOrderId;
            var result = await _apiCallRunner.RunAsync(
                () => _managerApi.SearchAsync(new PurchaseOrderManagerSearchRequest
                {
                    Keyword = Keyword.Trim(),
                    OrderNo = OrderNo.Trim(),
                    SourceNo = SourceNo.Trim(),
                    SupplierKeyword = SupplierKeyword.Trim(),
                    Sku = Sku.Trim(),
                    ConfirmationStatuses = SelectedConfirmationStatuses.Select(static option => option.Value).ToList(),
                    LifecycleStatuses = SelectedLifecycleStatuses.Select(static option => option.Value).ToList(),
                    PlannedDeliveryDateFrom = PlannedDeliveryDateFrom,
                    PlannedDeliveryDateTo = PlannedDeliveryDateTo,
                    ExpectedCompletionDateFrom = ExpectedCompletionDateFrom,
                    ExpectedCompletionDateTo = ExpectedCompletionDateTo,
                    PageIndex = PageIndex,
                    PageSize = PageSize
                }, cts.Token),
                cts.Token,
                "加载采购订单失败");

            if (cts.IsCancellationRequested || !ReferenceEquals(_queryCancellation, cts)) return false;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = GetFailureText(result, "采购订单加载失败。");
                return false;
            }

            DetachOrderEvents();
            Orders.Clear();
            foreach (var item in result.Data.Items)
            {
                var order = item.Adapt<PurchaseOrderManagementRowModel>();
                order.IsChecked = preserveCheckedIds?.Contains(order.PurchaseOrderId) == true;
                ApplyPendingConfirmationState(item, order);
                order.PropertyChanged += OnOrderPropertyChanged;
                Orders.Add(order);
            }
            TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex;
            SelectedOrder = Orders.FirstOrDefault(x => x.PurchaseOrderId == selectedId) ?? Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张新链采购订单，本页 {Orders.Count} 张。";
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
        _detailCancellation?.Cancel(); _detailCancellation?.Dispose(); var cts = new CancellationTokenSource(); _detailCancellation = cts;
        Lines.Clear(); SelectedLine = null;
        if (order is null) { DetailStatusText = "请选择采购订单查看行摘要。"; return; }
        DetailStatusText = "正在加载采购行...";
        var result = await _managerApi.GetLinesAsync(order.PurchaseOrderId, cts.Token);
        if (!cts.IsCancellationRequested && ReferenceEquals(_detailCancellation, cts) && SelectedOrder?.PurchaseOrderId == order.PurchaseOrderId)
        {
            if (result.Success && result.Data is not null) { foreach (var line in result.Data) Lines.Add(line.Adapt<PurchaseOrderManagementLineModel>()); SelectedLine = Lines.FirstOrDefault(); DetailStatusText = $"共 {Lines.Count} 条采购行。"; }
            else DetailStatusText = result.Error?.Message ?? result.Message;
        }
        if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null; cts.Dispose();
    }

    private bool CanOpenDetail() => HasReadPermission && IsNotBusy && SelectedOrder is not null;
    private bool CanQuery() => HasReadPermission && IsNotBusy;
    private bool CanBatchConfirm() => HasReadPermission && HasConfirmPermission && IsNotBusy
        && Orders.Any(order => order.IsChecked && !HasPendingConfirmation(order.PurchaseOrderId));
    private bool CanCreateIndependent() => HasCreatePermission && IsNotBusy;
    private bool CanConfirmOrder() => HasReadPermission && HasConfirmPermission && IsNotBusy
        && SelectedOrder?.CanConfirm == true
        && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    private bool CanCancelOrderConfirmation() => HasConfirmPermission && IsNotBusy
        && SelectedOrder is { LifecycleStatus: PurchaseOrderLifecycleStatusEnum.Active, ConfirmationStatus: ConfirmationStatusEnum.已确认 };

    /// <summary>同步当前页表头三态、已选数量和批量命令可执行状态。</summary>
    private void RefreshSelectionState()
    {
        var selectedCount = SelectedCount;
        _synchronizingSelection = true;
        try
        {
            IsAllCurrentPageChecked = Orders.Count == 0 || selectedCount == 0
                ? false
                : selectedCount == Orders.Count ? true : null;
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

    /// <summary>判断确认上下文是否包含必须在详情页人工绑定的客供 BOM 节点。</summary>
    private static bool RequiresConsumptionBinding(ProcurementConfirmationContextDto context) => context.Lines
        .SelectMany(static line => line.Bom.Nodes)
        .Any(static node => node.IsCustomerSupplied);

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
        _pendingConfirmations[order.PurchaseOrderId] = new PendingConfirmationState(command);
        order.MarkConfirmationResultUnknown();
        BatchConfirmCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>查询当前订单是否存在尚未由权威已确认状态消解的写入结果。</summary>
    private bool HasPendingConfirmation(Guid purchaseOrderId) => _pendingConfirmations.ContainsKey(purchaseOrderId);

    /// <summary>把会话未决状态投影到重建行；仅权威查询明确返回已确认时解除门禁。</summary>
    private void ApplyPendingConfirmationState(
        PurchaseOrderManagerListItemDto source,
        PurchaseOrderManagementRowModel target)
    {
        if (!_pendingConfirmations.ContainsKey(source.PurchaseOrderId)) return;
        if (source.ConfirmationStatus == ConfirmationStatusEnum.已确认)
        {
            _pendingConfirmations.Remove(source.PurchaseOrderId);
            return;
        }
        target.MarkConfirmationResultUnknown();
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

    /// <summary>保存结果未决确认的原始命令身份与完整负载；当前页面不自动重放。</summary>
    private sealed record PendingConfirmationState(ConfirmProcurementOrderCommand Command);

    public void Dispose() { DetachOrderEvents(); _queryCancellation?.Cancel(); _queryCancellation?.Dispose(); _detailCancellation?.Cancel(); _detailCancellation?.Dispose(); }
}
