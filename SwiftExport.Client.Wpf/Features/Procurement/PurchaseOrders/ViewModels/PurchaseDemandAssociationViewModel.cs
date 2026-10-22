using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Caching;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>独立采购需求关联工作区；选择、暂存、服务端预览及冻结命令均由本页承载。</summary>
public sealed partial class PurchaseDemandAssociationViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>固定工作区路由；从采购详情打开时不接收当前单据或行。</summary>
    public const string RouteKey = "procurement-demand-association";
    private readonly IProcurementOrderApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private readonly ILogger<PurchaseDemandAssociationViewModel> _logger;
    private CancellationTokenSource? _query;
    private PendingProcurementDemandAssociation? _pending;
    private int _minimumReadVersion;
    private long _planRevision;
    private bool _disposed, _initialized;

    /// <summary>组合只读采购选择区与本页命令状态，不包装重复 API 转发服务。</summary>
    public PurchaseDemandAssociationViewModel(IProcurementOrderApi api, IPurchaseOrderManagerApi manager,
        IApiCallRunner runner, IDialogService dialogs, ICurrentUserService user,
        ILogger<PurchaseDemandAssociationViewModel> logger, IReferenceDataCacheService? referenceData = null)
    {
        _api = api; _runner = runner; _dialogs = dialogs; _user = user; _logger = logger;
        Title = "关联物料需求";
        OrderPicker = new(manager, runner, () => CanInteract, referenceData, logger);
        OrderPicker.PropertyChanged += OnPickerChanged;
        PropertyChanged += OnPageChanged;
        _user.CurrentUserChanged += OnUserChanged;
        foreach (var command in new IRelayCommand[] { ChooseOrderCommand, RefreshCommand, SelectLineCommand,
            SearchCandidatesCommand, PreviousCandidatePageCommand, NextCandidatePageCommand,
            AddAllocationCommand, RemoveAllocationCommand, PreviewCommand, SubmitCommand, RetryCommand })
            RegisterBusyAwareCommand(command);
    }
    /// <summary>仅显示仍有未关联数量的采购单。</summary>
    public AssociationOrderPickerViewModel OrderPicker { get; }
    /// <summary>本单所有活动行，零剩余量或异常行展示原因。</summary>
    public ObservableCollection<ProcurementDemandAssociationLineDto> Lines { get; } = [];
    /// <summary>所选行当前页物料候选；翻页不清除暂存分配。</summary>
    public ObservableCollection<ProcurementDemandCandidateDto> Candidates { get; } = [];
    /// <summary>未提交意图，与服务端当前有效引用分开展示。</summary>
    public ObservableCollection<ProcurementDemandAssociationPlanRow> Plan { get; } = [];
    [ObservableProperty] public partial ProcurementDemandAssociationContextDto? Context { get; private set; }
    [ObservableProperty] public partial ProcurementDemandAssociationLineDto? SelectedLine { get; set; }
    [ObservableProperty] public partial ProcurementDemandCandidateDto? SelectedCandidate { get; set; }
    [ObservableProperty] public partial ProcurementDemandAssociationPlanRow? SelectedPlanRow { get; set; }
    [ObservableProperty] public partial string CandidateKeyword { get; set; } = string.Empty;
    [ObservableProperty] public partial int CandidatePage { get; private set; } = 1;
    [ObservableProperty] public partial int CandidateTotal { get; private set; }
    [ObservableProperty] public partial decimal AllocationQuantity { get; set; } = 1;
    [ObservableProperty] public partial bool AcceptNonStandardMatch { get; set; }
    [ObservableProperty] public partial string Reason { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; private set; } = "请选择一张采购单，然后选择需要关联的采购行。";
    [ObservableProperty] public partial ProcurementDemandAssociationPreviewDto? Preview { get; private set; }
    [ObservableProperty] public partial bool NeedsReload { get; private set; }
    [ObservableProperty] public partial bool RefreshRequired { get; private set; }
    [ObservableProperty] public partial bool IsPickerOpen { get; set; } = true;
    [ObservableProperty] public partial int ReviewTabIndex { get; set; }
    /// <summary>供需提交 WPF 编辑事务的按钮使用，业务命令仍自行复核门禁。</summary>
    public bool CanAddNow => CanAdd();
    /// <summary>预览按钮门禁；View 先提交本地编辑，再调用命令。</summary>
    public bool CanPreviewNow => CanPreview();
    /// <summary>提交按钮门禁；View 先检查转换错误，再调用命令。</summary>
    public bool CanSubmitNow => CanSubmit();
    /// <summary>查询列表与关联接口分别沿用读取及编辑权限。</summary>
    public bool HasPermission => _user.HasPermission(PermissionKeys.ProcurementOrderRead) && _user.HasPermission(PermissionKeys.ProcurementOrderEdit);
    /// <summary>未知写入结果只能重试原命令，禁止编辑或替换采购。</summary>
    public bool HasPendingCommand => _pending is not null;
    /// <summary>统一页面交互门禁；写入结果未知时冻结当前上下文。</summary>
    public bool CanInteract => !_disposed && HasPermission && !IsBusy && !HasPendingCommand;
    /// <summary>写成功后必须先重读事实，才能继续分配。</summary>
    public bool CanEditPlan => CanInteract && Context is not null && !RefreshRequired && !NeedsReload;
    /// <summary>固定页签不随所选采购变为文档页签。</summary>
    public string DocumentTitle => "关联物料需求";
    /// <summary>关闭时保护暂存意图和尚未明确的写入结果。</summary>
    public bool HasUnsavedChanges => Plan.Count > 0 || HasPendingCommand;
    /// <summary>采购头来源优先；未指定来源时由首条暂存分配固定。</summary>
    public string SourceHint => Context?.SourceUid is not null ? $"限定来源：{Context.SourceNo}"
        : Plan.FirstOrDefault() is { } row ? $"本次限定来源：{row.Demand.SourceNo}" : "首条分配确定来源单；一张采购仅关联同一张来源单。";
    /// <summary>只初始化一次，重复激活保留在途操作。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        if (!HasPermission) { StatusText = "关联物料需求需要采购读取及编辑权限。"; return; }
        await OrderPicker.InitializeAsync();
    }
    /// <summary>写入期间及结果未知时不能关闭，未提交清单必须明确放弃。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
        => !IsBusy && !HasPendingCommand && (Plan.Count == 0 || await _dialogs.ConfirmAsync(
            "关闭关联页面", "暂存分配尚未提交，确定放弃吗？", "放弃并关闭", "继续处理", cancellationToken));

    private bool CanChoose() => CanInteract && !OrderPicker.IsBusy && OrderPicker.SelectedOrder is not null;
    [RelayCommand(CanExecute = nameof(CanChoose))]
    private async Task ChooseOrderAsync()
    {
        if (!CanChoose()) return;
        var order = OrderPicker.SelectedOrder!;
        if (Context?.PurchaseOrderId == order.PurchaseOrderId) return;
        if (Plan.Count > 0 && !await _dialogs.ConfirmAsync("切换采购单", "切换将放弃当前暂存分配。", "放弃并切换")) return;
        if (!CanChoose() || OrderPicker.SelectedOrder?.PurchaseOrderId != order.PurchaseOrderId) return;
        // 先读取成功才替换上下文，查询失败不丢失当前操作。
        await RunBusyAsync(() => LoadContextCoreAsync(order.PurchaseOrderId, replaceOrder: true), "读取关联上下文...");
    }
    private bool CanRefresh() => CanInteract && Context is not null;
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        await RunBusyAsync(() => LoadContextCoreAsync(Context!.PurchaseOrderId, replaceOrder: false), "重读关联依据...");
    }
    private bool CanSelectLine(ProcurementDemandAssociationLineDto? line) => CanEditPlan && line is not null;
    [RelayCommand(CanExecute = nameof(CanSelectLine))]
    private async Task SelectLineAsync(ProcurementDemandAssociationLineDto? line)
    {
        if (!CanSelectLine(line) || !Lines.Contains(line!)) return;
        SelectedLine = line; SelectedCandidate = null; Candidates.Clear();
        AcceptNonStandardMatch = false;
        if (!string.IsNullOrWhiteSpace(line!.BlockReason)) { StatusText = line.BlockReason; return; }
        await RunBusyAsync(() => LoadCandidatesCoreAsync(1), "读取物料候选...");
    }
    private bool CanSearchCandidates() => CanEditPlan && SelectedLine is not null && string.IsNullOrWhiteSpace(SelectedLine.BlockReason);
    [RelayCommand(CanExecute = nameof(CanSearchCandidates))]
    private Task SearchCandidatesAsync() => RunBusyAsync(() => LoadCandidatesCoreAsync(1), "读取物料候选...");
    [RelayCommand(CanExecute = nameof(CanSearchCandidates))]
    private Task PreviousCandidatePageAsync() => RunBusyAsync(() => LoadCandidatesCoreAsync(Math.Max(1, CandidatePage - 1)), "读取物料候选...");
    [RelayCommand(CanExecute = nameof(CanSearchCandidates))]
    private Task NextCandidatePageAsync() => RunBusyAsync(() => LoadCandidatesCoreAsync(Math.Min(Math.Max(1, (CandidateTotal + 29) / 30), CandidatePage + 1)), "读取物料候选...");
    private bool CanAdd() => CanEditPlan && SelectedLine is not null && SelectedCandidate is not null;
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddAllocation()
    {
        if (!CanAdd()) return;
        var line = SelectedLine!; var demand = SelectedCandidate!;
        if (!Lines.Contains(line) || !Candidates.Contains(demand)) { StatusText = "候选已变化，请重新选择。"; return; }
        var previous = Plan.FirstOrDefault(x => x.Line.PurchaseOrderLineId == line.PurchaseOrderLineId && x.Demand.NetDemandId == demand.NetDemandId);
        // 更新同一配对时先排除原意图，聚合校验以完整暂存清单为准。
        var row = new ProcurementDemandAssociationPlanRow { Line = line, Demand = demand, Quantity = AllocationQuantity, AcceptNonStandardMatch = AcceptNonStandardMatch };
        var proposed = Plan.Where(x => !ReferenceEquals(x, previous)).Append(row).ToArray();
        if (ValidatePlan(proposed, requireReviewed: false) is { } error) { StatusText = error; return; }
        if (previous is not null) { previous.PropertyChanged -= OnPlanChanged; Plan.Remove(previous); }
        row.PropertyChanged += OnPlanChanged; Plan.Add(row); SelectedPlanRow = row;
        InvalidatePreview(); StatusText = previous is null ? "已加入暂存清单，尚未提交。" : "已更新该配对并复核最新依据，尚未提交。";
    }
    private bool CanRemove() => CanInteract && SelectedPlanRow is not null;
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveAllocation()
    {
        if (!CanRemove()) return;
        var row = SelectedPlanRow!; row.PropertyChanged -= OnPlanChanged; Plan.Remove(row); SelectedPlanRow = null;
        InvalidatePreview(); Candidates.Clear(); SelectedCandidate = null;
        StatusText = "仅移除尚未提交的分配；请重新查询候选。";
    }
    /// <summary>本地检查显式意图和各自单位下的容量；服务端仍负责产品、修订及客供父级关系。</summary>
    private string? ValidatePlan(IEnumerable<ProcurementDemandAssociationPlanRow> rows, bool requireReviewed = true)
    {
        var items = rows.ToArray();
        if (items.Length is < 1 or > 500) return "请暂存 1 至 500 条分配。";
        if (requireReviewed && items.Any(x => x.NeedsReview)) return "重读后请逐条重新选择候选并加入清单，或移除待复核项。";
        if (items.Any(x => x.Quantity <= 0 || decimal.Round(x.Quantity, 4) != x.Quantity)) return "分配数量必须大于零，最多四位小数。";
        if (items.Any(x => !string.IsNullOrWhiteSpace(x.Line.BlockReason))) return "所选采购行存在阻断原因。";
        if (items.Any(x => string.IsNullOrWhiteSpace(x.Line.Unit) || string.IsNullOrWhiteSpace(x.Demand.Unit)
            || !string.Equals(x.Line.Unit.Trim(), x.Demand.Unit.Trim(), StringComparison.OrdinalIgnoreCase))) return "采购和需求单位必须明确且一致。";
        if (items.Any(x => x.Line.IsNonStandard != x.Demand.IsNonStandard || (x.Line.IsNonStandard && !x.AcceptNonStandardMatch))) return "非标匹配需人工核对双方描述和单位，并明确认可。";
        if (items.Any(x => !x.Line.IsNonStandard && (!x.Line.ProductId.HasValue || x.Line.ProductId == Guid.Empty
            || x.Line.ProductId != x.Demand.ProductId))) return "标准产品身份不匹配。";
        if (items.Select(x => (x.Demand.SourceType, x.Demand.SourceUid)).Distinct().Count() != 1
            || (Context?.SourceUid is { } uid && items.Any(x => x.Demand.SourceUid != uid || x.Demand.SourceType != Context.SourceType))) return "一张采购单只能关联同一张来源单。";
        if (items.GroupBy(x => x.Line.PurchaseOrderLineId).Any(g => g.Sum(x => x.Quantity) > (g.First().Line.UnlinkedQty ?? 0))) return "暂存分配合计超过采购行未关联数量。";
        if (items.FirstOrDefault(x => !x.Demand.CreatableQty.HasValue) is { } unknown)
            return string.IsNullOrWhiteSpace(unknown.Demand.ReviewReason) ? "物料可创建数量待核实，请刷新候选。" : unknown.Demand.ReviewReason;
        if (items.GroupBy(x => x.Demand.NetDemandId).Any(g => g.Sum(x => x.Quantity) > g.First().Demand.CreatableQty!.Value)) return "暂存分配合计超过物料需求可创建数量。";
        return null;
    }
    /// <summary>所有意图编辑撤销旧预览，不能继续提交旧数量或旧非标认可。</summary>
    private void InvalidatePreview()
    {
        _planRevision++; Preview = null; ReviewTabIndex = 0;
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(SourceHint)); NotifyCommands();
    }
    private void OnPlanChanged(object? sender, PropertyChangedEventArgs args) => InvalidatePreview();
    private void OnPickerChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(OrderPicker.SelectedOrder) or nameof(OrderPicker.IsBusy)) ChooseOrderCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedCandidateChanged(ProcurementDemandCandidateDto? value) => AcceptNonStandardMatch = false;
    private void OnPageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsBusy) or nameof(SelectedLine) or nameof(SelectedCandidate) or nameof(SelectedPlanRow)
            or nameof(Preview) or nameof(Reason) or nameof(NeedsReload) or nameof(RefreshRequired)) NotifyCommands();
    }
    private void OnUserChanged(object? sender, EventArgs args) { OnPropertyChanged(nameof(HasPermission)); NotifyCommands(); }
    /// <summary>同步所有派生门禁，集合变化也须通知命令。</summary>
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanInteract)); OnPropertyChanged(nameof(CanEditPlan)); OnPropertyChanged(nameof(HasPendingCommand));
        OnPropertyChanged(nameof(CanAddNow)); OnPropertyChanged(nameof(CanPreviewNow)); OnPropertyChanged(nameof(CanSubmitNow));
        ChooseOrderCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); SelectLineCommand.NotifyCanExecuteChanged();
        SearchCandidatesCommand.NotifyCanExecuteChanged(); PreviousCandidatePageCommand.NotifyCanExecuteChanged(); NextCandidatePageCommand.NotifyCanExecuteChanged();
        AddAllocationCommand.NotifyCanExecuteChanged(); RemoveAllocationCommand.NotifyCanExecuteChanged(); PreviewCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged(); RetryCommand.NotifyCanExecuteChanged(); OrderPicker.NotifyGateChanged();
    }
    /// <summary>记录意外异常并保留未决命令，不把异常正文直接交给用户。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        _pending?.MarkUnknown();
        _logger.LogError(exception, "采购需求关联页面操作失败");
        StatusText = HasPendingCommand ? "提交结果尚未明确，请重试原命令。"
            : RefreshRequired ? "关联已成功，但刷新失败；请重读关联依据，勿重新提交。" : "操作失败，请重试当前操作。";
        return Task.CompletedTask;
    }
    private void ClearPlan()
    {
        foreach (var row in Plan) row.PropertyChanged -= OnPlanChanged;
        Plan.Clear(); SelectedPlanRow = null; InvalidatePreview();
    }
    /// <summary>只接收 View 的编辑事务失败提示，不在 View 中处理分配业务规则。</summary>
    public void ReportEditorError() => StatusText = "存在尚未完成的编辑或数量格式错误，请修正后继续。";
    /// <summary>释放查询与订阅；在途请求即使忽略取消，也不得回写关闭页面。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _query?.Cancel(); OrderPicker.Dispose();
        PropertyChanged -= OnPageChanged; OrderPicker.PropertyChanged -= OnPickerChanged; _user.CurrentUserChanged -= OnUserChanged;
        foreach (var row in Plan) row.PropertyChanged -= OnPlanChanged;
    }
}
