using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>
/// 采购订单跟进工作台，负责按采购头行浏览供应商进度、登记累计反馈并展示不可覆盖的反馈历史。
/// </summary>
public sealed partial class PurchaseOrderProgressWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "procurement-purchase-order-progress";
    private readonly IPurchaseOrderProgressApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _restoringSelection;
    private bool _initialized;

    public PurchaseOrderProgressWorkspaceViewModel(
        IPurchaseOrderProgressApi api,
        IApiCallRunner runner,
        IDialogService dialogs,
        IMessageService messages)
    {
        _api = api;
        _runner = runner;
        _dialogs = dialogs;
        _messages = messages;
        Title = "采购订单跟进";
        ApplyDefaultStatuses();
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(ResetFiltersCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(LoadSelectedOrderCommand);
        RegisterBusyAwareCommand(OpenDrawerCommand);
        RegisterBusyAwareCommand(ReportProgressCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<PurchaseOrderProgressListItemDto> Orders { get; } = [];
    public ObservableCollection<PurchaseOrderProgressLineModel> Lines { get; } = [];

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
    public bool CanReportSelectedOrder => SelectedOrder?.CanReportProgress == true;
    public string FollowUpPermissionText => SelectedOrder is null
        ? "请选择采购单和采购行。"
        : CanReportSelectedOrder
            ? "当前采购单允许登记供应商反馈。"
            : string.IsNullOrWhiteSpace(SelectedOrder.ReadOnlyReason)
                ? "仅活动且已确认的采购单允许登记供应商反馈。"
                : SelectedOrder.ReadOnlyReason;

    /// <summary>当前输入是否会形成一条保留原历史的累计数量纠错报告。</summary>
    public bool IsProgressCorrection => SelectedLine?.LatestProgress is { } latest && ReportReadyQty < latest.ReadyQty;

    /// <summary>向跟单员明确说明累计数量下降的追加式纠错语义。</summary>
    public string ProgressCorrectionWarning => IsProgressCorrection
        ? $"累计已备好数量将从 {SelectedLine!.LatestReadyQty:#,##0.####} 下调为 {ReportReadyQty:#,##0.####}，系统会新增纠错报告并保留原历史；请填写纠错原因。"
        : string.Empty;

    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierKeyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateTo { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedReadyDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedReadyDateTo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDrawerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReportProgressCommand))]
    [NotifyPropertyChangedFor(nameof(CanReportSelectedOrder))]
    [NotifyPropertyChangedFor(nameof(FollowUpPermissionText))]
    public partial PurchaseOrderProgressListItemDto? SelectedOrder { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDrawerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReportProgressCommand))]
    [NotifyPropertyChangedFor(nameof(IsProgressCorrection))]
    [NotifyPropertyChangedFor(nameof(ProgressCorrectionWarning))]
    public partial PurchaseOrderProgressLineModel? SelectedLine { get; set; }

    [ObservableProperty] public partial bool IsDrawerOpen { get; set; }

    /// <summary>保留数量原始输入，以区分空白与无效数字，防止绑定转换失败后提交旧值。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportReadyQty))]
    [NotifyPropertyChangedFor(nameof(HasInvalidReadyQty))]
    [NotifyPropertyChangedFor(nameof(IsProgressCorrection))]
    [NotifyPropertyChangedFor(nameof(ProgressCorrectionWarning))]
    public partial string ReportReadyQtyInput { get; set; } = string.Empty;

    /// <summary>按当前客户端文化解析累计数量，留空不更新。</summary>
    public decimal? ReportReadyQty => decimal.TryParse(ReportReadyQtyInput,
        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var value) ? value : null;

    /// <summary>非空但不能转换为数量的输入必须阻止提交。</summary>
    public bool HasInvalidReadyQty => !string.IsNullOrWhiteSpace(ReportReadyQtyInput) && !ReportReadyQty.HasValue;

    [ObservableProperty] public partial DateTime? ReportExpectedReadyDate { get; set; }
    [ObservableProperty] public partial string ReportNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string CorrectionReason { get; set; } = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载采购单。";
    [ObservableProperty] public partial string DetailStatusText { get; set; } = "请选择采购单查看跟进明细。";

    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return LoadPageAsync();
    }

    partial void OnSelectedOrderChanged(PurchaseOrderProgressListItemDto? value)
    {
        IsDrawerOpen = false;
    }

    partial void OnSelectedLineChanged(PurchaseOrderProgressLineModel? value)
    {
        ReportReadyQtyInput = string.Empty;
        ReportExpectedReadyDate = null;
        ReportNote = string.Empty;
        CorrectionReason = string.Empty;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => LoadPageAsync(SelectedOrder?.PurchaseOrderId, SelectedLine?.Uid);

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        OrderNo = SupplierKeyword = Sku = string.Empty;
        PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedReadyDateFrom = ExpectedReadyDateTo = null;
        ApplyDefaultStatuses();
        PageIndex = 1;
        await LoadPageAsync();
    }

    /// <summary>由视图选择事件显式加载当前采购单，避免在绑定属性 setter 中执行异步 API。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSelectedOrder), AllowConcurrentExecutions = true)]
    private Task LoadSelectedOrderAsync()
    {
        if (_restoringSelection) return Task.CompletedTask;
        return LoadDetailAsync(SelectedOrder);
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedLine))]
    private void OpenDrawer() => IsDrawerOpen = true;

    [RelayCommand]
    private void CloseDrawer() => IsDrawerOpen = false;

    /// <summary>独立提交本次数量、交期或备注；由后台形成完整快照并追加历史。</summary>
    [RelayCommand(CanExecute = nameof(CanReportProgress))]
    private async Task ReportProgressAsync()
    {
        var order = SelectedOrder;
        var line = SelectedLine;
        if (!CanReportProgress() || order is null || line is null) return;
        if (HasInvalidReadyQty)
        {
            _messages.ShowWarning("已备好数量无效", "请输入有效数字，或留空表示本次不更新数量。");
            return;
        }
        if (!ReportReadyQty.HasValue && !ReportExpectedReadyDate.HasValue && string.IsNullOrWhiteSpace(ReportNote))
        {
            _messages.ShowWarning("尚未填写跟进", "请至少填写累计已备好数量、预计交期或跟进备注中的一项。");
            return;
        }
        if (ReportReadyQty < 0 || ReportReadyQty > line.Quantity)
        {
            _messages.ShowWarning("已备好数量无效", $"累计已备好数量必须在 0 与采购数量 {line.Quantity:#,##0.####} 之间。");
            return;
        }

        if (IsProgressCorrection && string.IsNullOrWhiteSpace(CorrectionReason))
        {
            _messages.ShowWarning("需要纠错原因", "累计已备好数量下降时必须填写纠错原因。");
            return;
        }

        // 在确认对话框前固定输入，避免等待期间切换行或编辑内容改变本次命令。
        var command = new ReportPurchaseOrderProgressCommand
        {
            CommandId = Guid.NewGuid(),
            PurchaseOrderLineId = line.Uid,
            ExpectedPurchaseOrderVersion = order.Version,
            ExpectedLineVersion = line.Version,
            ReadyQty = ReportReadyQty,
            ExpectedReadyDate = ReportExpectedReadyDate,
            Note = ReportNote,
            CorrectionReason = CorrectionReason
        };
        var fields = new List<string>();
        if (command.ReadyQty.HasValue) fields.Add($"累计已备好 {command.ReadyQty:#,##0.####}");
        if (command.ExpectedReadyDate.HasValue) fields.Add($"预计交期 {command.ExpectedReadyDate:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(command.Note)) fields.Add("跟进备注");
        var summary = string.Join("；", fields);
        var confirmation = $"采购行 {line.LineNumber}：{summary}。" + (IsProgressCorrection ? "将新增纠错报告并保留原历史。" : "");

        await RunWriteAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("登记供应商反馈", confirmation, "登记")) return;
            var result = await _runner.RunAsync(() => _api.ReportAsync(order.PurchaseOrderId, command), "登记供应商反馈失败");
            if (!result.Success)
            {
                _messages.ShowError("登记失败", result.Message);
                return;
            }

            _messages.ShowSuccess("供应商反馈已登记", $"{line.Sku}：{summary}");
            await LoadPageAsync(order.PurchaseOrderId, line.Uid, ownsBusy: false);
        }, "正在登记供应商反馈...");
    }

    [RelayCommand]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        return LoadPageAsync();
    }

    /// <summary>加载采购单分页，并按 Uid 恢复采购单和采购行选择。</summary>
    private async Task LoadPageAsync(Guid? preferredOrderId = null, Guid? preferredLineId = null, bool ownsBusy = true)
    {
        _searchCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCancellation = cts;
        PurchaseOrderProgressListItemDto? orderToLoad = null;
        // 写后刷新复用外层 Busy，避免同一用户操作嵌套占用页面忙状态。
        async Task LoadCoreAsync()
        {
            var selectedId = preferredOrderId ?? SelectedOrder?.PurchaseOrderId;
            var result = await _runner.RunAsync(() => _api.SearchAsync(CreateRequest(), cts.Token), cts.Token, "查询采购订单跟进失败");
            if (cts.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cts)) return;

            Orders.Clear();
            if (!result.Success || result.Data is null)
            {
                TotalCount = 0;
                SetSelectedOrder(null);
                Lines.Clear();
                ResultSummary = result.Message;
                return;
            }

            foreach (var item in result.Data.Items) Orders.Add(item);
            TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex;
            orderToLoad = Orders.FirstOrDefault(x => x.PurchaseOrderId == selectedId) ?? Orders.FirstOrDefault();
            SetSelectedOrder(orderToLoad);
            ResultSummary = $"共 {TotalCount} 张采购单，本页 {Orders.Count} 张。";
        }
        if (ownsBusy) await RunBusyAsync(LoadCoreAsync, "正在加载采购订单跟进...", false);
        else await LoadCoreAsync();

        if (ReferenceEquals(_searchCancellation, cts)) _searchCancellation = null;
        cts.Dispose();
        if (orderToLoad is not null) await LoadDetailAsync(orderToLoad, preferredLineId);
    }

    /// <summary>加载所选采购单行明细，并阻止慢旧请求覆盖用户最新选择。</summary>
    private async Task LoadDetailAsync(PurchaseOrderProgressListItemDto? order, Guid? preferredLineId = null)
    {
        _detailCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _detailCancellation = cts;
        var orderId = order?.PurchaseOrderId;
        IsDrawerOpen = false;
        SelectedLine = null;
        Lines.Clear();
        if (!orderId.HasValue)
        {
            DetailStatusText = "请选择采购单查看跟进明细。";
            cts.Dispose();
            if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null;
            return;
        }

        DetailStatusText = "正在加载采购行跟进明细...";
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(orderId.Value, cts.Token), cts.Token, "加载采购行跟进明细失败");
            if (cts.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cts) || SelectedOrder?.PurchaseOrderId != orderId.Value) return;
            if (!result.Success || result.Data is null)
            {
                DetailStatusText = string.IsNullOrWhiteSpace(result.Message) ? "采购行跟进明细加载失败。" : result.Message;
                return;
            }

            foreach (var item in result.Data.Lines.Select(PurchaseOrderProgressLineModel.FromDto)) Lines.Add(item);
            SelectedLine = Lines.FirstOrDefault(x => x.Uid == preferredLineId) ?? Lines.FirstOrDefault();
            DetailStatusText = Lines.Count == 0 ? "该采购单没有采购行。" : $"共 {Lines.Count} 条采购行。";
        }
        finally
        {
            if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null;
            cts.Dispose();
        }
    }

    private void SetSelectedOrder(PurchaseOrderProgressListItemDto? order)
    {
        _restoringSelection = true;
        try { SelectedOrder = order; }
        finally { _restoringSelection = false; }
    }

    private void ApplyDefaultStatuses()
    {
        SelectedConfirmationStatuses.Clear();
        SelectedLifecycleStatuses.Clear();
        SelectedConfirmationStatuses.Add(ConfirmationOptions.Single(x => x.Value == ConfirmationStatusEnum.已确认));
        SelectedLifecycleStatuses.Add(LifecycleOptions.Single(x => x.Value == PurchaseOrderLifecycleStatusEnum.Active));
    }

    private PurchaseOrderProgressSearchRequest CreateRequest() => new()
    {
        OrderNo = OrderNo.Trim(),
        SupplierKeyword = SupplierKeyword.Trim(),
        Sku = Sku.Trim(),
        ConfirmationStatuses = SelectedConfirmationStatuses.Select(x => x.Value).Distinct().ToList(),
        LifecycleStatuses = SelectedLifecycleStatuses.Select(x => x.Value).Distinct().ToList(),
        PlannedDeliveryDateFrom = PlannedDeliveryDateFrom,
        PlannedDeliveryDateTo = PlannedDeliveryDateTo,
        ExpectedReadyDateFrom = ExpectedReadyDateFrom,
        ExpectedReadyDateTo = ExpectedReadyDateTo,
        PageIndex = PageIndex,
        PageSize = PageSize
    };

    private bool CanLoadSelectedOrder() => IsNotBusy && SelectedOrder is not null;
    private bool CanUseSelectedLine() => IsNotBusy && SelectedOrder is not null && SelectedLine is not null;
    private bool CanReportProgress() => CanWrite && IsNotBusy && SelectedOrder?.CanReportProgress == true && SelectedLine is not null;
}
