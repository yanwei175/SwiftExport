using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购订单管理页模型；只负责订单查询、主从只读摘要和进入详情、预览等专页。</summary>
public sealed partial class PurchaseOrderManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-orders";
    private readonly IPurchaseOrderManagerApi _managerApi;
    private readonly IProcurementOrderApi _orderApi;
    private readonly IProcurementDialogService _procurementDialogs;
    private readonly INetDemandDialogService _netDemandDialogs;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IPurchaseOrderCancellationDialogService _cancellationDialog;
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _queryCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _initialized;

    public PurchaseOrderManagerViewModel(IPurchaseOrderManagerApi managerApi, IProcurementOrderApi orderApi,
        IProcurementDialogService procurementDialogs,
        INetDemandDialogService netDemandDialogs, INavigationService navigation, IDialogService dialogs,
        IMessageService messages, IPurchaseOrderCancellationDialogService cancellationDialog,
        ICurrentUserService currentUser)
    {
        _managerApi = managerApi; _orderApi = orderApi; _procurementDialogs = procurementDialogs;
        _netDemandDialogs = netDemandDialogs; _navigation = navigation; _dialogs = dialogs;
        _messages = messages; _cancellationDialog = cancellationDialog;
        _currentUser = currentUser; Title = "采购订单管理";
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand);
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

    public async Task InitializeAsync()
    {
        if (_initialized) return; _initialized = true;
        if (!HasReadPermission) { ResultSummary = "当前账号没有采购订单读取权限。"; return; }
        await LoadPageAsync();
    }

    partial void OnSelectedOrderChanged(PurchaseOrderManagementRowModel? value) => _ = LoadDetailAsync(value);
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task SearchAsync() { PageIndex = 1; await LoadPageAsync(); }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task RefreshAsync() => LoadPageAsync();
    [RelayCommand]
    private Task ClearFiltersAsync()
    {
        Keyword = OrderNo = SourceNo = SupplierKeyword = Sku = string.Empty;
        SelectedConfirmationStatuses.Clear();
        SelectedLifecycleStatuses.Clear();
        PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null;
        PageIndex = 1;
        return LoadPageAsync();
    }
    [RelayCommand] private Task PageChangeAsync(PageChangeRequest? request) { if (request is null) return Task.CompletedTask; PageIndex = request.NewIndex; PageSize = request.NewSize; return LoadPageAsync(); }
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey,
        $"purchase-order:new:{SelectedOrder!.PurchaseOrderId:N}",
        new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedOrder.PurchaseOrderId });
    [RelayCommand(CanExecute = nameof(CanConfirmOrder))]
    private async Task ConfirmAsync()
    {
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
            var result = await _orderApi.ConfirmAsync(order.PurchaseOrderId, new ConfirmProcurementOrderCommand
            {
                CommandId = Guid.NewGuid(),
                ExpectedVersion = context.Data.Version,
                ContextFingerprint = context.Data.Fingerprint,
                ConsumptionBindings = []
            });
            if (!result.Success || result.Data is null)
            {
                await HandleCommandFailureAsync(result, "确认采购失败", order.PurchaseOrderId);
                return;
            }

            await LoadPageCoreAsync(order.PurchaseOrderId);
            _messages.ShowSuccess("采购已确认", order.OrderNo);
        }, "正在确认采购订单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelOrderConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        var order = SelectedOrder!;
        var reason = await _cancellationDialog.RequestReasonAsync(order.OrderNo);
        if (reason is null)
            return;

        await RunWriteAsync(async () =>
        {
            var result = await _orderApi.CancelConfirmationAsync(order.PurchaseOrderId,
                new CancelProcurementOrderConfirmationCommand
                {
                    CommandId = Guid.NewGuid(),
                    ExpectedVersion = order.Version,
                    Reason = reason
                });
            if (!result.Success || result.Data is null)
            {
                await HandleCommandFailureAsync(result, "取消采购确认失败", order.PurchaseOrderId);
                return;
            }

            await LoadPageCoreAsync(order.PurchaseOrderId);
            _messages.ShowSuccess("已取消采购确认", order.OrderNo);
        }, "正在取消采购确认...");
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
        var rows = await _procurementDialogs.SelectNetDemandsAsync();
        if (rows.Count == 0) return;
        var applied = await _netDemandDialogs.ShowPurchaseDraftPreviewAsync(
            new PurchaseDraftPreviewOpenRequest
            {
                EntryMode = PurchaseDraftEntryModeEnum.PurchaseWorkspace,
                Rows = rows
            });
        if (applied) await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (!HasReadPermission) { ResultSummary = "当前账号没有采购订单读取权限。"; return; }
        ValidationSummary = null;
        await RunBusyAsync(() => LoadPageCoreAsync(), "正在加载采购订单...", false);
        if (!string.IsNullOrWhiteSpace(ValidationSummary))
            ResultSummary = $"采购订单加载失败：{ValidationSummary}";
    }

    /// <summary>执行可替代的管理分页查询；调用方负责最外层 Busy，并可指定刷新后应恢复的订单。</summary>
    private async Task LoadPageCoreAsync(Guid? preservePurchaseOrderId = null)
    {
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _queryCancellation = cts;
        try
        {
            var selectedId = preservePurchaseOrderId ?? SelectedOrder?.PurchaseOrderId;
            var result = await _managerApi.SearchAsync(new PurchaseOrderManagerSearchRequest
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
            }, cts.Token);

            if (cts.IsCancellationRequested || !ReferenceEquals(_queryCancellation, cts)) return;
            Orders.Clear();
            if (!result.Success || result.Data is null) { TotalCount = 0; SelectedOrder = null; ResultSummary = result.Error?.Message ?? result.Message; return; }
            foreach (var item in result.Data.Items) Orders.Add(item.Adapt<PurchaseOrderManagementRowModel>());
            TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex;
            SelectedOrder = Orders.FirstOrDefault(x => x.PurchaseOrderId == selectedId) ?? Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张新链采购订单，本页 {Orders.Count} 张。";
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
    private bool CanCreateIndependent() => HasCreatePermission && IsNotBusy;
    private bool CanConfirmOrder() => HasReadPermission && HasConfirmPermission && IsNotBusy && SelectedOrder?.CanConfirm == true;
    private bool CanCancelOrderConfirmation() => HasConfirmPermission && IsNotBusy && SelectedOrder?.CanCancelConfirmation == true;

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
    public void Dispose() { _queryCancellation?.Cancel(); _queryCancellation?.Dispose(); _detailCancellation?.Cancel(); _detailCancellation?.Dispose(); }
}
