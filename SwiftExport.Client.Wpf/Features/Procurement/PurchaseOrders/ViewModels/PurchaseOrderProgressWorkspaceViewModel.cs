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
using SwiftExportClient.Navigation;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>
/// 供应商反馈录入页，按采购头行登记累计反馈及纠错，保留不可覆盖的反馈历史。
/// </summary>
public sealed partial class PurchaseOrderProgressWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>Shell 供应商反馈选单页的路由。</summary>
    public const string RouteKey = "procurement-purchase-order-progress";
    /// <summary>订单反馈文档使用独立路由，避免 Shell 菜单被既有订单文档抢占。</summary>
    public const string DocumentRouteKey = "procurement-purchase-order-feedback";
    private readonly IPurchaseOrderProgressApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly ICurrentUserService? _currentUser;
    private readonly INavigationService? _navigation;
    private readonly PurchaseOrderProgressCommandSession _commandSession;
    private readonly string _ownerUser;
    private Guid _documentOrderId;
    private bool _contextStale;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _restoringSelection;
    private bool _initialized;
    private bool _disposed;
    private int _sessionVersion;

    /// <summary>注入反馈 API、页面反馈服务及应用内未决会话，不与采购编辑命令混用回执。</summary>
    public PurchaseOrderProgressWorkspaceViewModel(
        IPurchaseOrderProgressApi api,
        IApiCallRunner runner,
        IDialogService dialogs,
        IMessageService messages,
        ICurrentUserService? currentUser = null,
        INavigationService? navigation = null,
        PurchaseOrderProgressCommandSession? commandSession = null)
    {
        _api = api;
        _runner = runner;
        _dialogs = dialogs;
        _messages = messages;
        _currentUser = currentUser;
        _navigation = navigation;
        _commandSession = commandSession ?? new PurchaseOrderProgressCommandSession("test");
        _ownerUser = UserIdentity;
        _commandSession.Changed += OnCommandSessionChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        PropertyChanged += OnPageChanged;
        Title = "供应商反馈录入";
        ApplyDefaultStatuses();
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(ResetFiltersCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(LoadSelectedOrderCommand);
        RegisterBusyAwareCommand(OpenDrawerCommand);
        RegisterBusyAwareCommand(ReportProgressCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(RetryReportCommand);
    }

    /// <summary>Shell 选单结果，不承担整单进度汇总。</summary>
    public ObservableCollection<PurchaseOrderProgressListItemDto> Orders { get; } = [];
    /// <summary>指定采购单的全量采购行及反馈依据。</summary>
    public ObservableCollection<PurchaseOrderProgressLineModel> Lines { get; } = [];
    /// <summary>独立反馈录入工作区标题，进度查询由订单跟踪负责。</summary>
    public string DocumentTitle => IsOrderDocument ? $"供应商反馈 · {CurrentOrder?.OrderNo ?? "采购单"}" : "供应商反馈录入";
    /// <summary>包括抽屉中尚未提交的数量原文、日期、备注及纠错原因。</summary>
    public bool HasUnsavedChanges => HasPendingReport || !string.IsNullOrWhiteSpace(ReportReadyQtyInput) || ReportExpectedReadyDate.HasValue
        || !string.IsNullOrWhiteSpace(ReportNote) || !string.IsNullOrWhiteSpace(CorrectionReason);
    /// <summary>输入尚未处理时禁止查询、换单或换行覆盖它。</summary>
    public bool CanChangeSelection => !_disposed && IsNotBusy && HasReadPermission && !HasUnsavedChanges;
    /// <summary>当前订单存在必须以原身份重试的反馈，查询和新入口均不能覆盖。</summary>
    public bool HasPendingReport => _commandSession.Find(_ownerUser, _documentOrderId) is not null;
    /// <summary>确认窗口、提交及未决期间冻结输入；订单状态与账号权限分别核验。</summary>
    public bool CanEditReport => IsOrderDocument && CanReportSelectedOrder && CanWrite && IsNotBusy && !HasPendingReport && !_contextStale;
    /// <summary>解释查询和切换被阻断的原因，不让按钮点击后静默无效。</summary>
    public string SelectionBlockReason => HasPendingReport ? "反馈结果未确认，请先重试原反馈。"
        : HasUnsavedChanges ? "当前反馈尚未提交；提交或关闭录入并放弃后才能查询、分页或换行。"
        : !HasReadPermission ? "当前会话没有供应商反馈查看权限。" : string.Empty;
    private string UserIdentity => _currentUser is null ? "test" : _currentUser.IsAuthenticated ? _currentUser.CurrentUser?.UserId ?? string.Empty : string.Empty;
    private bool HasReadPermission => UserIdentity == _ownerUser && (_currentUser?.HasPermission(PermissionKeys.ProcurementProgressView) ?? true);
    /// <summary>执行中不能关闭，未提交反馈须明确放弃。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || HasPendingReport) return false;
        if (!HasUnsavedChanges) return true;
        var discard = await _dialogs.ConfirmAsync("关闭供应商反馈", "当前反馈尚未提交，是否放弃并关闭？", "放弃并关闭", "继续录入", cancellationToken);
        return discard && IsNotBusy && !HasPendingReport;
    }

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
    /// <summary>读取权限、登记权限和订单资格分别核验，服务端仍是最终权威。</summary>
    public bool CanReportSelectedOrder => HasReadPermission && CurrentOrder?.CanReportProgress == true
        && (_currentUser?.HasPermission(PermissionKeys.ProcurementProgressReport) ?? true);
    /// <summary>分别说明账号权限、未决命令、读取失效或订单状态导致的登记限制。</summary>
    public string FollowUpPermissionText => !HasReadPermission
        ? "当前会话没有供应商反馈查看权限。"
        : !(_currentUser?.HasPermission(PermissionKeys.ProcurementProgressReport) ?? true)
        ? "当前账号没有供应商反馈登记权限；可以查看反馈历史。"
        : HasPendingReport ? "反馈结果尚未确认，请重试原反馈。"
        : _contextStale ? "反馈依据需要刷新，核实后才能继续登记。"
        : CurrentOrder is null
        ? "请选择采购单和采购行。"
        : CanReportSelectedOrder
            ? "当前采购单允许登记供应商反馈。"
            : string.IsNullOrWhiteSpace(CurrentOrder.ReadOnlyReason)
                ? "仅活动且已确认的采购单允许登记供应商反馈。"
                : CurrentOrder.ReadOnlyReason;

    /// <summary>由 ID 直达时使用权威详情提供订单版本与登记资格，不依赖选单分页命中。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle), nameof(CanReportSelectedOrder), nameof(FollowUpPermissionText), nameof(CanEditReport))]
    public partial PurchaseOrderProgressDetailDto? CurrentOrder { get; private set; }
    /// <summary>订单文档与 Shell 选单共用反馈视图，文档隐藏选单区域。</summary>
    [ObservableProperty] public partial bool IsOrderDocument { get; private set; }

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
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(CanChangeSelection))]
    public partial string ReportReadyQtyInput { get; set; } = string.Empty;

    /// <summary>按当前客户端文化解析累计数量，留空不更新。</summary>
    public decimal? ReportReadyQty => decimal.TryParse(ReportReadyQtyInput,
        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var value) ? value : null;

    /// <summary>非空但不能转换为数量的输入必须阻止提交。</summary>
    public bool HasInvalidReadyQty => !string.IsNullOrWhiteSpace(ReportReadyQtyInput) && !ReportReadyQty.HasValue;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(CanChangeSelection))] public partial DateTime? ReportExpectedReadyDate { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(CanChangeSelection))] public partial string ReportNote { get; set; } = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnsavedChanges), nameof(CanChangeSelection))] public partial string CorrectionReason { get; set; } = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPages))] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载采购单。";
    [ObservableProperty] public partial string DetailStatusText { get; set; } = "请选择采购单查看反馈明细。";

    /// <summary>Shell 入口只初始化选单；登记统一进入稳定订单文档。</summary>
    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return HasReadPermission ? LoadPageAsync() : Task.CompletedTask;
    }

    /// <summary>直接以采购单 ID 获取当前反馈上下文，订单无需出现在默认选单页。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_initialized || _disposed || parameter is not PurchaseOrderProgressOpenRequest request || request.PurchaseOrderId == Guid.Empty) return;
        _initialized = true; IsOrderDocument = true; _documentOrderId = request.PurchaseOrderId;
        if (!HasReadPermission) { DetailStatusText = "当前会话没有供应商反馈查看权限。"; return; }
        var session = _sessionVersion;
        await RunBusyAsync(() => LoadDetailAsync(_documentOrderId, request.PurchaseOrderLineId, cancellationToken), "正在加载反馈依据...");
        if (_disposed || session != _sessionVersion || !HasReadPermission || cancellationToken.IsCancellationRequested) return;
        RestorePendingInput();
        IsDrawerOpen = SelectedLine is not null || HasPendingReport;
    }

    /// <summary>再次打开仅在干净、可读且非忙碌时定位已加载的真实行，拒绝覆盖输入和未决反馈。</summary>
    public void AcceptOpenRequest(PurchaseOrderProgressOpenRequest request)
    {
        if (!IsOrderDocument || request.PurchaseOrderId != _documentOrderId || !CanChangeSelection) return;
        if (request.PurchaseOrderLineId is { } line && Lines.FirstOrDefault(x => x.Uid == line) is { } selected) SelectedLine = selected;
        IsDrawerOpen = SelectedLine is not null;
    }

    partial void OnSelectedOrderChanged(PurchaseOrderProgressListItemDto? value)
    {
        IsDrawerOpen = false;
    }

    partial void OnSelectedLineChanged(PurchaseOrderProgressLineModel? value)
    {
        // 撤权清空只读行时，真实绑定会把选择回写为 null；这不能隐式放弃原始输入或未决反馈。
        if (HasPendingReport || (HasUnsavedChanges && (_contextStale || !HasReadPermission))) return;
        ReportReadyQtyInput = string.Empty;
        ReportExpectedReadyDate = null;
        ReportNote = string.Empty;
        CorrectionReason = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (IsOrderDocument || !CanChangeSelection) return;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => !CanChangeSelection ? Task.CompletedTask : IsOrderDocument
        ? RunBusyAsync(() => LoadDetailAsync(_documentOrderId, SelectedLine?.Uid), "正在刷新反馈依据...")
        : LoadPageAsync(SelectedOrder?.PurchaseOrderId, SelectedLine?.Uid);

    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task ResetFiltersAsync()
    {
        if (IsOrderDocument || !CanChangeSelection) return;
        OrderNo = SupplierKeyword = Sku = string.Empty;
        PlannedDeliveryDateFrom = PlannedDeliveryDateTo = null;
        ApplyDefaultStatuses();
        PageIndex = 1;
        await LoadPageAsync();
    }

    /// <summary>由视图选择事件显式加载当前采购单，避免在绑定属性 setter 中执行异步 API。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadSelectedOrder), AllowConcurrentExecutions = true)]
    private Task LoadSelectedOrderAsync()
    {
        if (_restoringSelection) return Task.CompletedTask;
        return RunBusyAsync(() => LoadDetailAsync(SelectedOrder?.PurchaseOrderId), "正在加载反馈依据...");
    }

    /// <summary>选单入口与采购管理/详情复用同一订单文档；文档内只展开已有录入抽屉。</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelectedLine))]
    private async Task OpenDrawerAsync()
    {
        if (!CanUseSelectedLine()) return;
        if (IsOrderDocument) IsDrawerOpen = true;
        else if (_navigation is not null && CurrentOrder is not null)
            await PurchaseOrderProgressNavigation.OpenAsync(_navigation, CurrentOrder.PurchaseOrderId, SelectedLine?.Uid);
    }

    [RelayCommand]
    private async Task CloseDrawerAsync()
    {
        if (!await CanCloseAsync(CancellationToken.None)) return;
        ClearReportInput();
        IsDrawerOpen = false;
    }

    /// <summary>独立提交本次数量、交期或备注；由后台形成完整快照并追加历史。</summary>
    [RelayCommand(CanExecute = nameof(CanReportProgress))]
    private async Task ReportProgressAsync()
    {
        var order = CurrentOrder;
        var line = SelectedLine;
        var session = _sessionVersion;
        if (!CanReportProgress() || order is null || line is null) return;
        if (HasInvalidReadyQty)
        {
            _messages.ShowWarning("已备好数量无效", "请输入有效数字，或留空表示本次不更新数量。");
            return;
        }
        if (!ReportReadyQty.HasValue && !ReportExpectedReadyDate.HasValue && string.IsNullOrWhiteSpace(ReportNote))
        {
            _messages.ShowWarning("尚未填写反馈", "请至少填写累计已备好数量、预计交期或反馈备注中的一项。");
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
        if (!string.IsNullOrWhiteSpace(command.Note)) fields.Add("反馈备注");
        var summary = string.Join("；", fields);
        var confirmation = $"采购行 {line.LineNumber}：{summary}。" + (IsProgressCorrection ? "将新增纠错报告并保留原历史。" : "");

        await RunWriteAsync(async () =>
        {
            if (!await _dialogs.ConfirmAsync("登记供应商反馈", confirmation, "登记")) return;
            // 确认窗口等待期间会话、权限或选择可能变化；旧身份不能发出反馈写入。
            if (_disposed || session != _sessionVersion || !CanReportSelectedOrder || _contextStale
                || CurrentOrder != order || SelectedLine != line || HasPendingReport) return;
            if (!_commandSession.Freeze(_ownerUser, order.PurchaseOrderId, command)) return;
            await SendFrozenReportAsync(order.PurchaseOrderId, session);
        }, "正在登记供应商反馈...");
    }

    /// <summary>重试原反馈不重新读取输入、版本或生成 CommandId，也不重复请求用户确认。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryReport))]
    private async Task RetryReportAsync()
    {
        if (!CanRetryReport()) return;
        var session = _sessionVersion;
        await RunWriteAsync(() => SendFrozenReportAsync(_documentOrderId, session), "正在核实原反馈...");
    }

    /// <summary>明确成功先接纳输入，再进行只读刷新；刷新失败不得使下一次操作重发反馈。</summary>
    private async Task SendFrozenReportAsync(Guid order, int session)
    {
        var pending = _commandSession.Find(_ownerUser, order);
        if (pending is null) return;
        var result = await _commandSession.SendAsync(_ownerUser, order, _api, _runner);
        if (_disposed || session != _sessionVersion || UserIdentity != _ownerUser) return;
        if (!result.Success)
        {
            DetailStatusText = result.Message;
            _messages.ShowError("登记供应商反馈失败", result.Message);
            return;
        }
        ClearReportInput();
        _contextStale = true; NotifyInteraction();
        _messages.ShowSuccess("供应商反馈已登记", "反馈已确认保存，正在刷新反馈依据。");
        await LoadDetailAsync(order, pending.PurchaseOrderLineId);
    }

    /// <summary>页面重建时从当前账号及环境的冻结命令恢复显示，输入副本不能改变重试负载。</summary>
    private void RestorePendingInput()
    {
        var pending = _commandSession.Find(_ownerUser, _documentOrderId);
        if (pending is null) return;
        SelectedLine = Lines.FirstOrDefault(x => x.Uid == pending.PurchaseOrderLineId) ?? SelectedLine;
        // 行定位和原始字段一并恢复；这些显示副本不会参与原命令重试。
        ReportReadyQtyInput = pending.ReadyQty?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        ReportExpectedReadyDate = pending.ExpectedReadyDate; ReportNote = pending.Note ?? string.Empty; CorrectionReason = pending.CorrectionReason ?? string.Empty;
        DetailStatusText = "反馈结果尚未确认，请重试原反馈。"; NotifyInteraction();
    }

    /// <summary>只在明确成功或用户明确放弃未提交输入后清理编辑。</summary>
    private void ClearReportInput()
    { ReportReadyQtyInput = string.Empty; ReportExpectedReadyDate = null; ReportNote = CorrectionReason = string.Empty; }

    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (IsOrderDocument || request is null || !CanChangeSelection) return Task.CompletedTask;
        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        return LoadPageAsync();
    }

    /// <summary>加载采购单分页，并按 Uid 恢复采购单和采购行选择。</summary>
    private async Task LoadPageAsync(Guid? preferredOrderId = null, Guid? preferredLineId = null)
    {
        if (_disposed || !HasReadPermission) return;
        _searchCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCancellation = cts;
        var session = _sessionVersion;
        PurchaseOrderProgressListItemDto? orderToLoad = null;
        // 写后刷新复用外层 Busy，避免同一用户操作嵌套占用页面忙状态。
        async Task LoadCoreAsync()
        {
            var selectedId = preferredOrderId ?? SelectedOrder?.PurchaseOrderId;
            var result = await _runner.RunAsync(() => _api.SearchAsync(CreateRequest(), cts.Token), cts.Token, "查询反馈选单失败");
            if (_disposed || session != _sessionVersion || !HasReadPermission || cts.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cts)) return;

            Orders.Clear();
            if (!result.Success || result.Data is null)
            {
                TotalCount = 0;
                SetSelectedOrder(null);
                Lines.Clear();
                CurrentOrder = null;
                ResultSummary = result.Message;
                return;
            }

            foreach (var item in result.Data.Items) Orders.Add(item);
            TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex;
            orderToLoad = Orders.FirstOrDefault(x => x.PurchaseOrderId == selectedId) ?? Orders.FirstOrDefault();
            SetSelectedOrder(orderToLoad);
            ResultSummary = $"共 {TotalCount} 张采购单，本页 {Orders.Count} 张。";
            await LoadDetailAsync(orderToLoad?.PurchaseOrderId, preferredLineId, cts.Token);
        }
        try { await RunBusyAsync(LoadCoreAsync, "正在加载反馈选单...", false); }
        finally { if (ReferenceEquals(_searchCancellation, cts)) _searchCancellation = null; cts.Dispose(); }
    }

    /// <summary>加载所选采购单行明细，并阻止慢旧请求覆盖用户最新选择。</summary>
    private async Task LoadDetailAsync(Guid? orderId, Guid? preferredLineId = null, CancellationToken cancellationToken = default)
    {
        if (_disposed || !HasReadPermission) return;
        _detailCancellation?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _detailCancellation = cts;
        var session = _sessionVersion;
        _contextStale = true; NotifyInteraction();
        if (!orderId.HasValue)
        {
            CurrentOrder = null; SelectedLine = null; Lines.Clear();
            DetailStatusText = "请选择采购单查看反馈依据。";
            cts.Dispose();
            if (ReferenceEquals(_detailCancellation, cts)) _detailCancellation = null;
            return;
        }

        DetailStatusText = "正在加载反馈依据...";
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(orderId.Value, cts.Token), cts.Token, "加载反馈依据失败");
            if (_disposed || session != _sessionVersion || !HasReadPermission || cts.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cts)
                || (IsOrderDocument ? _documentOrderId : SelectedOrder?.PurchaseOrderId) != orderId.Value) return;
            if (!result.Success || result.Data is null || result.Data.PurchaseOrderId != orderId)
            {
                DetailStatusText = "反馈依据加载失败，请点击刷新恢复读取。";
                return;
            }

            CurrentOrder = result.Data; SelectedLine = null; Lines.Clear();
            foreach (var item in result.Data.Lines.Select(PurchaseOrderProgressLineModel.FromDto)) Lines.Add(item);
            SelectedLine = Lines.FirstOrDefault(x => x.Uid == preferredLineId) ?? Lines.FirstOrDefault();
            _contextStale = false; NotifyInteraction();
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
        PageIndex = PageIndex,
        PageSize = PageSize
    };

    private bool CanQuery() => CanChangeSelection;
    private bool CanLoadSelectedOrder() => !IsOrderDocument && CanChangeSelection && SelectedOrder is not null;
    private bool CanUseSelectedLine() => !_disposed && HasReadPermission && IsNotBusy && !_contextStale && CurrentOrder is not null && SelectedLine is not null
        && (IsOrderDocument || SelectedOrder?.PurchaseOrderId == CurrentOrder.PurchaseOrderId);
    private bool CanReportProgress() => !_disposed && CanEditReport && SelectedLine is not null && Lines.Contains(SelectedLine);
    private bool CanRetryReport() => !_disposed && HasReadPermission && CanWrite && IsNotBusy && HasPendingReport
        && (_currentUser?.HasPermission(PermissionKeys.ProcurementProgressReport) ?? true);
    /// <summary>会话变化取消旧读取；页面绑定首次账号，不允许新账号发送旧账号未决命令。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        _sessionVersion++; _searchCancellation?.Cancel(); _detailCancellation?.Cancel();
        _contextStale = true;
        if (!HasReadPermission) { Orders.Clear(); Lines.Clear(); CurrentOrder = null; }
        DetailStatusText = "会话或权限已变化，请刷新核实反馈依据；未决命令只允许原账号重试。";
        NotifyInteraction();
    }
    /// <summary>任一输入、权限或共享会话变化都同步查询和登记命令，避免可点却无操作。</summary>
    private void NotifyInteraction()
    {
        OnPropertyChanged(nameof(HasPendingReport)); OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanChangeSelection)); OnPropertyChanged(nameof(CanEditReport));
        OnPropertyChanged(nameof(CanReportSelectedOrder)); OnPropertyChanged(nameof(FollowUpPermissionText)); OnPropertyChanged(nameof(SelectionBlockReason));
        SearchCommand.NotifyCanExecuteChanged(); ResetFiltersCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); PageChangeCommand.NotifyCanExecuteChanged();
        LoadSelectedOrderCommand.NotifyCanExecuteChanged(); OpenDrawerCommand.NotifyCanExecuteChanged(); ReportProgressCommand.NotifyCanExecuteChanged(); RetryReportCommand.NotifyCanExecuteChanged();
    }
    private void OnCommandSessionChanged(object? sender, EventArgs e) { if (!_disposed) NotifyInteraction(); }
    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsReadOnly) or nameof(ReportReadyQtyInput) or nameof(ReportExpectedReadyDate) or nameof(ReportNote) or nameof(CorrectionReason))
            NotifyInteraction();
    }
    /// <summary>取消页面查询并解除会话订阅，释放后迟到结果不得回写。</summary>
    public void Dispose()
    { if (_disposed) return; _disposed = true; _searchCancellation?.Cancel(); _detailCancellation?.Cancel(); _commandSession.Changed -= OnCommandSessionChanged; PropertyChanged -= OnPageChanged; if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged; }
}
