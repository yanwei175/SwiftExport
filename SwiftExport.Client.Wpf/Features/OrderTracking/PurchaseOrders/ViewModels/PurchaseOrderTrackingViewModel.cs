using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.OrderTracking.PurchaseOrders.ViewModels;

/// <summary>独立只读采购单跟踪：采购头分页，全量行、反馈及正式收货证据按采购身份独立读取。</summary>
public sealed partial class PurchaseOrderTrackingViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>固定采购视角入口，不从采购管理挂载。</summary>
    public const string RouteKey = "order-tracking-purchase-orders";
    private readonly IOrderTrackingApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private readonly INavigationService _navigation;
    private readonly IReferenceDataCacheService? _references;
    private readonly ILogger<PurchaseOrderTrackingViewModel> _logger;
    private CancellationTokenSource? _query, _details;
    private string? _appliedJson;
    private bool _initialized, _disposed;

    /// <summary>仅依赖订单跟踪只读API；客户和供应商候选复用现有基础资料缓存。</summary>
    public PurchaseOrderTrackingViewModel(IOrderTrackingApi api, IApiCallRunner runner, ICurrentUserService user, INavigationService navigation,
        IReferenceDataCacheService? referenceData = null, ILogger<PurchaseOrderTrackingViewModel>? logger = null)
    {
        _api = api; _runner = runner; _user = user; _navigation = navigation; _references = referenceData;
        _logger = logger ?? NullLogger<PurchaseOrderTrackingViewModel>.Instance;
        IsReadOnly = true; Title = DocumentTitle; _user.CurrentUserChanged += OnUserChanged;
    }
    /// <summary>独立固定工作区，重复打开复用原筛选和选择。</summary>
    public string DocumentTitle => "采购订单跟踪";
    /// <summary>只读跟踪无写入草稿。</summary>
    public bool HasUnsavedChanges => false;
    /// <summary>关闭后释放查询，不阻断其他模块的未决写命令。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    /// <summary>采购视角同时需要跟踪查看与采购读取。</summary>
    public bool CanView => !_disposed && _user.HasPermission(PermissionKeys.OrderTrackingView) && _user.HasPermission(PermissionKeys.ProcurementOrderRead);
    /// <summary>反馈维度沿原独立反馈查看权限。</summary>
    public bool CanViewProgress => CanView && _user.HasPermission(PermissionKeys.ProcurementProgressView);
    /// <summary>正式收货维度沿入库查看权限。</summary>
    public bool CanViewReceipts => CanView && _user.HasPermission(PermissionKeys.InventoryInboundView);
    /// <summary>明确区分缺少维度权限和业务数量未知。</summary>
    public string PermissionText => !CanView ? "需要订单跟踪查看及采购读取权限。"
        : string.Join("；", new[] { CanViewProgress ? null : "无供应商反馈查看权限，货好与交期风险条件禁用", CanViewReceipts ? null : "无入库查看权限，正式收货条件禁用" }.Where(x => x is not null));
    /// <summary>当前服务端页采购单，包括独立创建及需求创建采购。</summary>
    public ObservableCollection<PurchaseOrderTrackingRowModel> Orders { get; } = [];
    /// <summary>当前采购单全量行，不按主表条件裁剪。</summary>
    public ObservableCollection<PurchaseOrderTrackingLineModel> Lines { get; } = [];
    /// <summary>当前采购单全部追加反馈，保留每次纠错记录。</summary>
    public ObservableCollection<PurchaseOrderReportRowModel> Reports { get; } = [];
    /// <summary>正式收货及反向历史，不将草稿入库计入净收货。</summary>
    public ObservableCollection<DemandTrackingReceiptDto> Receipts { get; } = [];
    /// <summary>采购整行可信净收货，独立于需求贡献。</summary>
    public ObservableCollection<PurchaseLineReceiptProgressDto> ReceiptLines { get; } = [];
    public ReadOnlyObservableCollection<CustomerDto>? CustomerOptions => _references?.Customers;
    public ReadOnlyObservableCollection<SupplierDto>? SupplierOptions => _references?.Suppliers;
    public ObservableCollection<CustomerDto> SelectedCustomers { get; } = [];
    public ObservableCollection<SupplierDto> SelectedSuppliers { get; } = [];
    public ObservableCollection<ConfirmationStatusEnum> SelectedConfirmationStatuses { get; } = [];
    public ObservableCollection<PurchaseOrderLifecycleStatusEnum> SelectedLifecycleStatuses { get; } = [];
    public ObservableCollection<PurchaseReadyStatusEnum> SelectedReadyStatuses { get; } = [];
    public ObservableCollection<PurchaseReceiptStatusEnum> SelectedReceiptStatuses { get; } = [];
    public static IReadOnlyList<ConfirmationStatusEnum> ConfirmationOptions { get; } = Enum.GetValues<ConfirmationStatusEnum>();
    public static IReadOnlyList<PurchaseOrderLifecycleStatusEnum> LifecycleOptions { get; } = Enum.GetValues<PurchaseOrderLifecycleStatusEnum>();
    public static IReadOnlyList<PurchaseReadyStatusEnum> ReadyOptions { get; } = Enum.GetValues<PurchaseReadyStatusEnum>();
    public static IReadOnlyList<PurchaseReceiptStatusEnum> ReceiptOptions { get; } = Enum.GetValues<PurchaseReceiptStatusEnum>();
    public static IReadOnlyList<PurchaseDueViewEnum> DueOptions { get; } = Enum.GetValues<PurchaseDueViewEnum>();
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? OrderDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? OrderDateTo { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? PlannedDeliveryDateTo { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedCompletionDateTo { get; set; }
    [ObservableProperty] public partial PurchaseDueViewEnum SelectedDueView { get; set; }
    [ObservableProperty] public partial int PageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int PageSize { get; private set; } = 20;
    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty] public partial bool IsLoadingOrders { get; private set; }
    [ObservableProperty] public partial bool IsLoadingDetails { get; private set; }
    [ObservableProperty] public partial bool IsUpdatingOrders { get; private set; }
    [ObservableProperty] public partial string ResultSummary { get; private set; } = "尚未查询采购订单。";
    [ObservableProperty] public partial string LinesMessage { get; private set; } = "请选择采购订单查看全部行。";
    [ObservableProperty] public partial string ReportsMessage { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ReceiptsMessage { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ReferenceDataMessage { get; private set; } = string.Empty;
    [ObservableProperty] public partial int VisibleOrderCount { get; set; }
    [ObservableProperty] public partial int VisibleLineCount { get; set; }
    [ObservableProperty] public partial PurchaseOrderTrackingLineModel? SelectedLine { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadSelectedOrderCommand), nameof(OpenFactsCommand))]
    public partial PurchaseOrderTrackingRowModel? SelectedOrder { get; set; }
    /// <summary>初始化幂等，引用候选失败不阻止独立采购事实查询。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return; _initialized = true;
        var user = _user.CurrentUser?.UserId;
        var referenceMessage = string.Empty;
        if (_references is not null && CanView)
        {
            try { var loaded = await _references.EnsureLoadedAsync(); if (!loaded.Success) referenceMessage = "客户/供应商候选未加载，可继续按采购单号查询。"; }
            catch (Exception ex) { _logger.LogWarning(ex, "采购跟踪基础候选加载失败"); referenceMessage = "客户/供应商候选加载失败，可继续按采购单号查询。"; }
        }
        if (!_disposed && user == _user.CurrentUser?.UserId) { ReferenceDataMessage = referenceMessage; await SearchAsync(); }
    }
    partial void OnSelectedOrderChanged(PurchaseOrderTrackingRowModel? value) => ResetDetails();
    private bool CanQuery() => CanView;
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => QueryAsync(null, 1, PageSize);
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => QueryAsync(_appliedJson, PageIndex, PageSize);
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request) => request is null ? Task.CompletedTask : QueryAsync(_appliedJson, request.NewIndex, request.NewSize);
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task ClearFiltersAsync()
    {
        Keyword = string.Empty; SelectedCustomers.Clear(); SelectedSuppliers.Clear(); SelectedConfirmationStatuses.Clear(); SelectedLifecycleStatuses.Clear();
        SelectedReadyStatuses.Clear(); SelectedReceiptStatuses.Clear(); SelectedDueView = PurchaseDueViewEnum.All;
        OrderDateFrom = OrderDateTo = PlannedDeliveryDateFrom = PlannedDeliveryDateTo = ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null;
        return SearchAsync();
    }
    /// <summary>分页和刷新使用最后成功条件，失败保留原页与选择，迟到响应不得覆盖新查询。</summary>
    private async Task QueryAsync(string? applied, int page, int size)
    {
        if (!CanView) { ResultSummary = PermissionText; return; }
        var request = applied is null ? CaptureQuery() : JsonSerializer.Deserialize<PurchaseOrderTrackingSearchRequest>(applied)!;
        request.PageIndex = Math.Max(1, page); request.PageSize = Math.Clamp(size, 1, 200);
        if (request.OrderDateFrom > request.OrderDateTo || request.PlannedDeliveryDateFrom > request.PlannedDeliveryDateTo || request.ExpectedCompletionDateFrom > request.ExpectedCompletionDateTo)
        { ResultSummary = "日期起点不能晚于终点。"; return; }
        if (!CanViewProgress && (request.ReadyStatuses.Count > 0 || request.DueView != PurchaseDueViewEnum.All || request.ExpectedCompletionDateFrom.HasValue || request.ExpectedCompletionDateTo.HasValue)
            || !CanViewReceipts && request.ReceiptStatuses.Count > 0)
        { ResultSummary = "所选进度条件需要对应查看权限。"; return; }
        _query?.Cancel(); var cts = new CancellationTokenSource(); _query = cts; IsLoadingOrders = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchPurchaseOrdersAsync(request, cts.Token), cts.Token, "采购跟踪查询失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_query, cts)) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message + " 当前结果与页码保留。"; return; }
            var selected = SelectedOrder?.Data.PurchaseOrderId;
            IsUpdatingOrders = true;
            try { Orders.Clear(); foreach (var dto in result.Data.Items) Orders.Add(new(dto)); SelectedOrder = Orders.FirstOrDefault(x => x.Data.PurchaseOrderId == selected) ?? Orders.FirstOrDefault(); }
            finally { IsUpdatingOrders = false; }
            _appliedJson = JsonSerializer.Serialize(request); PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : request.PageIndex;
            PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : request.PageSize; TotalCount = result.Data.TotalCount;
            ResultSummary = $"共 {TotalCount} 张采购订单，本页 {Orders.Count} 张；下表按所选采购单全量读取。";
            await LoadSelectedOrderAsync();
        }
        catch (Exception ex) { if (!_disposed && !cts.IsCancellationRequested) { _logger.LogError(ex, "采购跟踪查询异常"); ResultSummary = "采购跟踪读取失败，当前结果保留。"; } }
        finally { if (ReferenceEquals(_query, cts)) { _query = null; IsLoadingOrders = false; } cts.Dispose(); }
    }
    private PurchaseOrderTrackingSearchRequest CaptureQuery() => new()
    {
        Keyword = Keyword.Trim(), CustomerCodes = SelectedCustomers.Select(x => x.CustomerCode).Distinct().ToList(), SupplierIds = SelectedSuppliers.Select(x => x.Uid).Distinct().ToList(),
        ConfirmationStatuses = SelectedConfirmationStatuses.ToList(), LifecycleStatuses = SelectedLifecycleStatuses.ToList(), ReadyStatuses = SelectedReadyStatuses.ToList(),
        ReceiptStatuses = SelectedReceiptStatuses.ToList(), DueView = SelectedDueView, OrderDateFrom = OrderDateFrom, OrderDateTo = OrderDateTo,
        PlannedDeliveryDateFrom = PlannedDeliveryDateFrom, PlannedDeliveryDateTo = PlannedDeliveryDateTo, ExpectedCompletionDateFrom = ExpectedCompletionDateFrom, ExpectedCompletionDateTo = ExpectedCompletionDateTo
    };
    private bool CanLoadSelected() => CanView && SelectedOrder is not null;
    /// <summary>反馈和正式收货独立加载；无权维度不调用接口，任一失败不伪造其他维度的零值。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSelected), AllowConcurrentExecutions = true)]
    private async Task LoadSelectedOrderAsync()
    {
        if (!CanLoadSelected()) return;
        var id = SelectedOrder!.Data.PurchaseOrderId; _details?.Cancel(); var cts = new CancellationTokenSource(); _details = cts; IsLoadingDetails = true;
        LinesMessage = "正在读取全部采购行…"; ReportsMessage = CanViewProgress ? "正在读取全部反馈…" : "无供应商反馈查看权限。";
        ReceiptsMessage = CanViewReceipts ? "正在读取正式收货证据…" : "无入库查看权限。";
        try
        {
            var linesTask = ReadEvidenceAsync(() => _runner.RunAsync(() => _api.GetPurchaseOrderLinesAsync(id, cts.Token), cts.Token, "采购行读取失败"), "采购行", cts.Token);
            var reportsTask = CanViewProgress ? ReadEvidenceAsync(() => _runner.RunAsync(() => _api.GetPurchaseOrderReportsAsync(id, cts.Token), cts.Token, "供应商反馈读取失败"), "供应商反馈", cts.Token) : null;
            var receiptsTask = CanViewReceipts ? ReadEvidenceAsync(() => _runner.RunAsync(() => _api.GetPurchaseOrderReceiptsAsync(id, cts.Token), cts.Token, "正式收货读取失败"), "正式收货", cts.Token) : null;
            var tasks = new List<Task> { linesTask }; if (reportsTask is not null) tasks.Add(reportsTask); if (receiptsTask is not null) tasks.Add(receiptsTask);
            await Task.WhenAll(tasks);
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_details, cts) || SelectedOrder?.Data.PurchaseOrderId != id) return;
            var lines = await linesTask;
            if (lines.Success && lines.Data is not null) { Lines.Clear(); foreach (var dto in lines.Data) Lines.Add(new(dto, SelectedOrder.Data)); LinesMessage = $"全量 {Lines.Count} 条采购行，不继承主表筛选。"; }
            else LinesMessage = lines.Message + " 可重新加载，已有本单数据保留。";
            if (reportsTask is not null)
            {
                var reports = await reportsTask;
                if (reports.Success && reports.Data is not null)
                { Reports.Clear(); foreach (var dto in reports.Data.OrderBy(x => x.PurchaseOrderLineId).ThenByDescending(x => x.Sequence)) Reports.Add(new(dto, Lines.FirstOrDefault(x => x.Data.PurchaseOrderLineId == dto.PurchaseOrderLineId)?.Data.LineNumber)); ReportsMessage = $"完整反馈 {Reports.Count} 条，包含追加纠错历史。"; }
                else ReportsMessage = reports.Message + " 读取失败，已有本单反馈保留。";
            }
            if (receiptsTask is not null)
            {
                var receipts = await receiptsTask;
                if (receipts.Success && receipts.Data?.PurchaseOrderId == id)
                { Receipts.Clear(); ReceiptLines.Clear(); foreach (var dto in receipts.Data.Items) Receipts.Add(dto); foreach (var dto in receipts.Data.PurchaseLines) ReceiptLines.Add(dto); ReceiptsMessage = $"正式收货及反向证据 {Receipts.Count} 条。" + receipts.Data.ReviewReason; }
                else ReceiptsMessage = receipts.Message + " 收货证据未接纳，已有本单数据保留。";
            }
        }
        catch (Exception ex) { if (!_disposed && !cts.IsCancellationRequested) { _logger.LogError(ex, "采购跟踪明细异常 {PurchaseOrderId}", id); LinesMessage = "采购跟踪明细读取失败，请重新加载。"; } }
        finally { if (ReferenceEquals(_details, cts)) { _details = null; IsLoadingDetails = false; } cts.Dispose(); }
    }
    /// <summary>只隔离本页各证据请求的意外异常；Runner已经分类的失败不重复记录或弹窗。</summary>
    private async Task<ServiceResult<T>> ReadEvidenceAsync<T>(Func<Task<ServiceResult<T>>> action, string name, CancellationToken token)
    {
        try { return await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return ServiceResult<T>.Fail(string.Empty); }
        catch (Exception ex) { _logger.LogError(ex, "采购跟踪证据读取异常 {Dimension}", name); return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, name + "读取失败，请重试。"); }
    }
    /// <summary>打开同一稳定身份的采购事实页签，不在只读跟踪中执行写命令。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSelected))]
    private Task OpenFactsAsync() => CanLoadSelected()
        ? _navigation.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey, $"purchase-order:new:{SelectedOrder!.Data.PurchaseOrderId:N}", new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = SelectedOrder.Data.PurchaseOrderId })
        : Task.CompletedTask;
    private void ResetDetails()
    {
        _details?.Cancel(); Lines.Clear(); Reports.Clear(); Receipts.Clear(); ReceiptLines.Clear(); SelectedLine = null;
        LinesMessage = "请选择采购订单或重新加载全部行。"; ReportsMessage = ReceiptsMessage = string.Empty;
    }
    private void OnUserChanged(object? sender, EventArgs e)
    {
        _query?.Cancel(); _details?.Cancel(); _appliedJson = null;
        if (!CanViewProgress) { SelectedReadyStatuses.Clear(); SelectedDueView = PurchaseDueViewEnum.All; ExpectedCompletionDateFrom = ExpectedCompletionDateTo = null; }
        if (!CanViewReceipts) SelectedReceiptStatuses.Clear();
        Orders.Clear(); SelectedOrder = null; ResetDetails(); TotalCount = 0; ResultSummary = "会话或权限已变化，请重新查询。";
        OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(CanViewProgress)); OnPropertyChanged(nameof(CanViewReceipts)); OnPropertyChanged(nameof(PermissionText));
        SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); PageChangeCommand.NotifyCanExecuteChanged(); ClearFiltersCommand.NotifyCanExecuteChanged();
    }
    /// <summary>对称取消两类查询并解除会话订阅，迟到结果不能回写关闭页面。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; _query?.Cancel(); _details?.Cancel(); _user.CurrentUserChanged -= OnUserChanged; }
}
