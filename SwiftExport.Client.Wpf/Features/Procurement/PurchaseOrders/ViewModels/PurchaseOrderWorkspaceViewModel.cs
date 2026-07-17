using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

public sealed partial class PurchaseOrderWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "procurement-purchase-orders";
    private readonly IPurchaseOrderWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IProcurementDialogService _procurementDialogs;
    private readonly INetDemandDialogService _netDemandDialogs;
    private readonly IPurchaseOrderTraceExportService _exportService;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _initialized;

    public PurchaseOrderWorkspaceViewModel(IPurchaseOrderWorkspaceApi api, IApiCallRunner runner, IDialogService dialogs,
        IMessageService messages, IProcurementDialogService procurementDialogs, INetDemandDialogService netDemandDialogs,
        IPurchaseOrderTraceExportService exportService)
    {
        _api = api; _runner = runner; _dialogs = dialogs; _messages = messages; _procurementDialogs = procurementDialogs;
        _netDemandDialogs = netDemandDialogs; _exportService = exportService; Title = "采购工作台";
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand); RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(SaveDraftCommand); RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(ReportProgressCommand); RegisterBusyAwareCommand(OpenTraceCommand); RegisterBusyAwareCommand(ExportCommand);
        RegisterBusyAwareCommand(GetFromNetDemandCommand); RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<PurchaseOrderListItemDto> Orders { get; } = [];
    public ObservableCollection<PurchaseOrderLineEditorModel> Lines { get; } = [];
    public ObservableCollection<BusinessOperationTraceItemDto> Operations { get; } = [];
    public IReadOnlyList<ConfirmationStatusEnum?> ConfirmationOptions { get; } = [null, ConfirmationStatusEnum.待确认, ConfirmationStatusEnum.已确认];
    public IReadOnlyList<PurchaseOrderLifecycleStatusEnum?> LifecycleOptions { get; } = [null, PurchaseOrderLifecycleStatusEnum.Active, PurchaseOrderLifecycleStatusEnum.Closed, PurchaseOrderLifecycleStatusEnum.Voided];
    public IReadOnlyList<SupplierArrangementStatusEnum?> ArrangementOptions { get; } = [null, SupplierArrangementStatusEnum.Unknown, SupplierArrangementStatusEnum.NotArranged, SupplierArrangementStatusEnum.Arranged, SupplierArrangementStatusEnum.NotApplicable];
    public IReadOnlyList<SupplierArrangementStatusEnum> ReportArrangementOptions { get; } = [SupplierArrangementStatusEnum.Unknown, SupplierArrangementStatusEnum.NotArranged, SupplierArrangementStatusEnum.Arranged, SupplierArrangementStatusEnum.NotApplicable];
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    public string ArrangementSummary => SelectedOrder is null ? string.Empty : $"已安排 {SelectedOrder.ArrangedLineCount}/{SelectedOrder.LineCount}";
    public string InboundText => "尚未接入";
    public string ShippedText => "尚未接入";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierKeyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial ConfirmationStatusEnum? ConfirmationStatus { get; set; }
    [ObservableProperty] public partial PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; set; }
    [ObservableProperty] public partial SupplierArrangementStatusEnum? ArrangementStatus { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateTo { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedReadyDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedReadyDateTo { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ArrangementSummary))]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))] [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelConfirmationCommand))] [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))] public partial PurchaseOrderListItemDto? SelectedOrder { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ReportProgressCommand))] public partial PurchaseOrderLineEditorModel? SelectedLine { get; set; }
    [ObservableProperty] public partial bool IsDrawerOpen { get; set; }
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    [ObservableProperty] public partial string CancellationReason { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierArrangementStatusEnum ReportArrangementStatus { get; set; } = SupplierArrangementStatusEnum.Unknown;
    [ObservableProperty] public partial decimal ReportReadyQty { get; set; }
    [ObservableProperty] public partial DateTime? ReportExpectedReadyDate { get; set; }
    [ObservableProperty] public partial DateTime ReportedDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string ReportNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string CorrectionReason { get; set; } = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载采购单。";

    public Task InitializeAsync() { if (_initialized) return Task.CompletedTask; _initialized = true; return LoadPageAsync(); }

    partial void OnSelectedOrderChanged(PurchaseOrderListItemDto? value) { _ = LoadDetailAsync(value); }
    partial void OnSelectedLineChanged(PurchaseOrderLineEditorModel? value)
    {
        if (value?.LatestProgress is { } latest)
        { ReportArrangementStatus = latest.ArrangementStatus; ReportReadyQty = latest.ReadyQty; ReportExpectedReadyDate = latest.ExpectedReadyDate; ReportedDate = DateTime.Today; }
        else { ReportArrangementStatus = SupplierArrangementStatusEnum.Unknown; ReportReadyQty = 0; ReportExpectedReadyDate = null; ReportedDate = DateTime.Today; }
        ReportNote = string.Empty; CorrectionReason = string.Empty;
    }

    [RelayCommand(AllowConcurrentExecutions = true)] private async Task SearchAsync() { PageIndex = 1; await LoadPageAsync(); }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task RefreshAsync() => LoadPageAsync();
    [RelayCommand] private async Task ClearFiltersAsync()
    { Keyword = OrderNo = SourceNo = SupplierKeyword = Sku = string.Empty; ConfirmationStatus = null; LifecycleStatus = null; ArrangementStatus = null; PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedReadyDateFrom = ExpectedReadyDateTo = null; PageIndex = 1; await LoadPageAsync(); }
    [RelayCommand] private Task CloseDrawerAsync() { IsDrawerOpen = false; return Task.CompletedTask; }

    [RelayCommand(CanExecute = nameof(CanSaveDraft))]
    private async Task SaveDraftAsync()
    {
        var order = SelectedOrder; if (order is null || !await _dialogs.ConfirmAsync("保存采购草稿", "只会修改计划交期、供应商 SKU、描述、价格和备注；数量、供应商及来源不会改变。是否继续？", "保存")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.UpdateDraftAsync(order.Uid, new UpdatePurchaseOrderDraftCommand
            {
                CommandId = Guid.NewGuid(), ExpectedVersion = order.Version, Remark = Remark,
                Lines = Lines.Select(x => new UpdatePurchaseOrderDraftLineCommand { PurchaseOrderLineId = x.Uid, ExpectedVersion = x.Version, PlannedDeliveryDate = x.PlannedDeliveryDate, SupplierSku = x.SupplierSku, DescriptionZh = x.DescriptionZh, UnitPrice = x.UnitPrice, TaxPrice = x.TaxPrice, SubNote = x.SubNote }).ToList()
            }), "保存采购草稿失败");
            if (!result.Success) { _messages.ShowError("保存失败", result.Message); return; }
            _messages.ShowSuccess("采购草稿已保存", order.OrderNo); await ReloadCurrentAsync(order.Uid);
        }, "正在保存采购草稿...");
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var order = SelectedOrder; if (order is null || !await _dialogs.ConfirmAsync("确认采购单", "确认前将校验供应商、价格、需求 Link 和客供件送达关系。明确的零价允许确认。", "确认")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ConfirmAsync(order.Uid, new ConfirmPurchaseOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = order.Version }), "确认采购单失败");
            if (!result.Success) { _messages.ShowError("确认失败", result.Message); return; }
            _messages.ShowSuccess("采购单已确认", order.OrderNo); await ReloadCurrentAsync(order.Uid);
        }, "正在确认采购单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        var order = SelectedOrder; if (order is null) return;
        if (string.IsNullOrWhiteSpace(CancellationReason)) { _messages.ShowWarning("需要取消原因", "请先在右侧抽屉填写取消确认原因。"); return; }
        if (!await _dialogs.ConfirmAsync("取消采购确认", "系统将检查收货和库存事实；历史确认信息保留在业务日志。是否继续？", "取消确认")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.CancelConfirmationAsync(order.Uid, new CancelPurchaseOrderConfirmationCommand { CommandId = Guid.NewGuid(), ExpectedVersion = order.Version, Reason = CancellationReason.Trim() }), "取消采购确认失败");
            if (!result.Success) { _messages.ShowError("取消确认失败", result.Message); return; }
            _messages.ShowSuccess("已取消确认", order.OrderNo); CancellationReason = string.Empty; await ReloadCurrentAsync(order.Uid);
        }, "正在取消采购确认...");
    }

    [RelayCommand(CanExecute = nameof(CanReportProgress))]
    private async Task ReportProgressAsync()
    {
        var order = SelectedOrder; var line = SelectedLine; if (order is null || line is null) return;
        if (ReportReadyQty < (line.LatestProgress?.ReadyQty ?? 0) && string.IsNullOrWhiteSpace(CorrectionReason)) { _messages.ShowWarning("需要纠错原因", "累计已备好数量下降时必须填写纠错原因。"); return; }
        if (!await _dialogs.ConfirmAsync("登记供应商反馈", $"将为采购行 {line.LineNumber} 新增一条不可覆盖的累计反馈，已备好 {ReportReadyQty:N4}。", "登记")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ReportProgressAsync(order.Uid, new SupplierProgressReportCommand
            {
                CommandId = Guid.NewGuid(), PurchaseOrderLineId = line.Uid, ExpectedPurchaseOrderVersion = order.Version, ExpectedLineVersion = line.Version,
                ArrangementStatus = ReportArrangementStatus, ReadyQty = ReportReadyQty, ExpectedReadyDate = ReportExpectedReadyDate,
                SupplierReportedDate = ReportedDate, Note = ReportNote, CorrectionReason = CorrectionReason
            }), "登记供应商反馈失败");
            if (!result.Success) { _messages.ShowError("登记失败", result.Message); return; }
            _messages.ShowSuccess("供应商反馈已登记", $"{line.Sku} 累计已备好 {ReportReadyQty:N4}"); await ReloadCurrentAsync(order.Uid);
        }, "正在登记供应商反馈...");
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task OpenTraceAsync()
    {
        var order = SelectedOrder; if (order is null) return;
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(order.Uid), "加载采购追溯失败");
        if (result.Success && result.Data is not null) await _dialogs.ShowContentAsync($"采购追溯 · {order.OrderNo}", new Views.PurchaseOrderTraceView { DataContext = result.Data });
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task ExportAsync()
    {
        var order = SelectedOrder; if (order is null) return;
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(order.Uid), "加载采购追溯失败");
        if (!result.Success || result.Data is null) { _messages.ShowError("导出失败", result.Message); return; }
        var path = await _exportService.ExportAsync(result.Data); if (path is not null) _messages.ShowSuccess("导出完成", path);
    }

    [RelayCommand]
    private async Task GetFromNetDemandAsync()
    {
        var rows = await _procurementDialogs.SelectNetDemandsAsync();
        if (rows.Count == 0) return;
        var applied = await _netDemandDialogs.ShowPurchaseDraftPreviewAsync(new PurchaseDraftPreviewOpenRequest { EntryMode = PurchaseDraftEntryModeEnum.PurchaseWorkspace, Rows = rows });
        if (applied) await LoadPageAsync();
    }

    [RelayCommand] private Task PageChangeAsync(PageChangeRequest? request) { if (request is null) return Task.CompletedTask; PageIndex = request.NewIndex; PageSize = request.NewSize; return LoadPageAsync(); }

    private async Task LoadPageAsync()
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); var cts = new CancellationTokenSource(); _searchCancellation = cts;
        await RunBusyAsync(async () =>
        {
            var selectedId = SelectedOrder?.Uid;
            var result = await _runner.RunAsync(() => _api.SearchAsync(CreateRequest(), cts.Token), cts.Token, "查询采购工作台失败");
            if (cts.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cts)) return;
            Orders.Clear();
            if (!result.Success || result.Data is null) { TotalCount = 0; SelectedOrder = null; ResultSummary = result.Message; return; }
            foreach (var item in result.Data.Items) Orders.Add(item);
            TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex; SelectedOrder = Orders.FirstOrDefault(x => x.Uid == selectedId) ?? Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张采购单，本页 {Orders.Count} 张。";
        }, "正在加载采购工作台...", false);
        if (ReferenceEquals(_searchCancellation, cts)) _searchCancellation = null; cts.Dispose();
    }

    private async Task LoadDetailAsync(PurchaseOrderListItemDto? order)
    {
        _detailCancellation?.Cancel(); _detailCancellation?.Dispose(); var cts = new CancellationTokenSource(); _detailCancellation = cts;
        Lines.Clear(); Operations.Clear(); IsDrawerOpen = order is not null; if (order is null) { cts.Dispose(); return; }
        var result = await _runner.RunAsync(() => _api.GetDetailAsync(order.Uid, cts.Token), cts.Token, "加载采购详情失败");
        if (!cts.IsCancellationRequested && ReferenceEquals(_detailCancellation, cts) && ReferenceEquals(order, SelectedOrder) && result.Success && result.Data is not null)
        {
            Remark = result.Data.Remark ?? string.Empty; foreach (var line in result.Data.Lines) Lines.Add(PurchaseOrderLineEditorModel.FromDto(line));
            foreach (var operation in result.Data.Operations) Operations.Add(operation); SelectedLine = Lines.FirstOrDefault();
            SaveDraftCommand.NotifyCanExecuteChanged();
        }
        if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null; cts.Dispose();
    }

    private async Task ReloadCurrentAsync(Guid uid) { await LoadPageAsync(); SelectedOrder = Orders.FirstOrDefault(x => x.Uid == uid) ?? SelectedOrder; }
    private PurchaseOrderSearchRequest CreateRequest() => new() { Keyword = Keyword.Trim(), OrderNo = OrderNo.Trim(), SourceNo = SourceNo.Trim(), SupplierKeyword = SupplierKeyword.Trim(), Sku = Sku.Trim(), ConfirmationStatus = ConfirmationStatus, LifecycleStatus = LifecycleStatus, ArrangementStatus = ArrangementStatus, PlannedDeliveryDateFrom = PlannedDeliveryDateFrom, PlannedDeliveryDateTo = PlannedDeliveryDateTo, ExpectedReadyDateFrom = ExpectedReadyDateFrom, ExpectedReadyDateTo = ExpectedReadyDateTo, PageIndex = PageIndex, PageSize = PageSize };
    private bool CanUseSelected() => IsNotBusy && SelectedOrder is not null;
    private bool CanSaveDraft() => CanWrite && IsNotBusy && SelectedOrder?.CanEditDraft == true && Lines.Count > 0;
    private bool CanConfirm() => CanWrite && IsNotBusy && SelectedOrder?.CanConfirm == true;
    private bool CanCancelConfirmation() => CanWrite && IsNotBusy && SelectedOrder?.CanCancelConfirmation == true;
    private bool CanReportProgress() => CanWrite && IsNotBusy && SelectedOrder?.CanReportProgress == true && SelectedLine is not null;
}
