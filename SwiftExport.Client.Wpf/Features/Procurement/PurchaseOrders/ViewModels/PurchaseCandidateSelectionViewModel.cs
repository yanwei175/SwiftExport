using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>负责按来源完整加载待采购物料，并在内存中筛选和维护当前选择。</summary>
public sealed partial class PurchaseCandidateSelectionViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseDraftApi _api;
    private readonly IApiCallRunner _runner;
    private readonly PurchasePreparationService? _preparationService;
    private CancellationTokenSource? _lifetimeCancellation;
    private CancellationTokenSource? _groupCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _supplierSearchCancellation;
    private PurchaseDraftWorkflowViewModel? _workflow;
    private int _selectionBatchDepth;
    private bool _selectionBatchChanged;
    private bool _disposed;

    /// <summary>当前准备输入通过验证后，请求容器显示只读审核页。</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>初始化采购候选选择器依赖和本地集合视图。</summary>
    public PurchaseCandidateSelectionViewModel(
        IPurchaseDraftApi api,
        PurchasePreparationService preparationService,
        IApiCallRunner runner)
    {
        _api = api;
        _preparationService = preparationService;
        _runner = runner;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = MatchesLocalFilter;
    }

    /// <summary>保留仅验证候选加载的测试入口；供应商搜索需使用完整应用依赖。</summary>
    public PurchaseCandidateSelectionViewModel(IPurchaseDraftApi api, IApiCallRunner runner)
    {
        _api = api;
        _runner = runner;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = MatchesLocalFilter;
    }

    /// <summary>全部入口及仍有采购候选的来源单。</summary>
    public ObservableCollection<PurchaseCandidateSourceGroupModel> SourceGroups { get; } = [];

    /// <summary>当前来源入口一次完整加载的采购候选。</summary>
    public ObservableCollection<PurchaseCandidateItemModel> Items { get; } = [];

    /// <summary>只对当前已加载集合应用筛选的 WPF 视图。</summary>
    public ICollectionView ItemsView { get; }

    public ObservableCollection<ProcurementSupplierCandidateDto> SupplierCandidates { get; } = [];

    public event EventHandler? InputChanged;

    /// <summary>当前会话的集中采购状态；页面通过它显示行模式和验证警告。</summary>
    public PurchaseDraftWorkflowViewModel? Workflow => _workflow;

    /// <summary>当前会话是否至少执行过一次采购校验。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationActionText))]
    public partial bool HasValidationAttempted { get; private set; }

    /// <summary>当前输入是否已取得可继续确认的服务端验证快照。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnterPreviewCommand))]
    public partial bool HasValidatedSelection { get; private set; }

    /// <summary>采购准备页的校验与恢复反馈。</summary>
    [ObservableProperty]
    public partial string SelectionValidationText { get; private set; } = string.Empty;

    /// <summary>重复校验始终保留入口，不受上一次服务端阻断影响。</summary>
    public string ValidationActionText => HasValidationAttempted ? "重新校验所选物料" : "校验所选物料";

    /// <summary>由视图桥接原生 DataGrid 编辑提交；返回 false 表示转换错误仍停留在当前单元格。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }

    /// <summary>返回已加载集合中全部勾选项；本地筛选不会丢失隐藏行选择。</summary>
    public IReadOnlyList<PurchaseCandidateItemModel> SelectedRows =>
        Items.Where(static item => item.IsChecked).ToArray();

    /// <summary>当前本地筛选后的可见候选数量。</summary>
    public int FilteredCount => ItemsView.Cast<object>().Count();

    /// <summary>当前已勾选候选数量，包含因本地筛选暂时隐藏的行。</summary>
    public int SelectedCount => Items.Count(static item => item.IsChecked);

    /// <summary>当前可见行是否全部勾选。</summary>
    public bool IsAllVisibleChecked
    {
        get
        {
            var visible = GetVisibleRows();
            return visible.Count > 0 && visible.All(static item => item.IsChecked);
        }
    }

    [ObservableProperty]
    public partial PurchaseCandidateSourceGroupModel? SelectedSourceGroup { get; private set; }

    [ObservableProperty]
    public partial PurchaseCandidateSourceGroupModel? PendingSourceGroup { get; private set; }

    /// <summary>是否正在等待用户在当前页面确认来源切换。</summary>
    public bool IsSourceSwitchPending => PendingSourceGroup is not null;

    /// <summary>来源切换确认提示；使用行内状态避免在统一采购弹窗内再打开嵌套 Dialog。</summary>
    public string SourceSwitchPrompt => PendingSourceGroup is null
        ? string.Empty
        : $"切换到 {PendingSourceGroup.DisplaySourceNo} 将清除当前已选的 {SelectedCount} 条物料。";

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceNo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOnlyErrors { get; set; }

    [ObservableProperty]
    public partial PurchaseCandidateItemModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsSupplierSearchBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAllVisibleCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertVisibleSelectionCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleAllVisibleCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertVisibleSelectionCommand))]
    public partial bool IsDetailLoaded { get; private set; }

    /// <summary>当前准备页是否允许修改来源、勾选和采购输入；恢复服务端事实期间统一关闭。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterPreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAllVisibleCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertVisibleSelectionCommand))]
    public partial bool IsInputEnabled { get; private set; } = true;

    [ObservableProperty]
    public partial string SourceGroupStatusText { get; private set; } = "正在加载待采购来源...";

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "请选择左侧来源查看待采购物料。";

    /// <summary>打开时只加载完整来源分组，右侧候选保持为空。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingRequests();
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ClearItems();
        SourceGroups.Clear();
        SelectedSourceGroup = null;
        PendingSourceGroup = null;
        HasValidationAttempted = false;
        HasValidatedSelection = false;
        SelectionValidationText = string.Empty;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
        IsDetailLoaded = false;
        SourceGroupStatusText = "正在加载待采购来源...";
        StatusText = "请选择左侧来源查看待采购物料。";
        await LoadGroupsAsync(_lifetimeCancellation.Token);
    }

    /// <summary>切换来源前确认清除已有选择，再以 latest-request-wins 加载完整候选。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectSourceGroupAsync(PurchaseCandidateSourceGroupModel? group)
    {
        if (!IsInputEnabled || group is null || _disposed
            || ReferenceEquals(SelectedSourceGroup, group)
            || _lifetimeCancellation is null
            || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (SelectedCount > 0)
        {
            PendingSourceGroup = group;
            OnPropertyChanged(nameof(IsSourceSwitchPending));
            OnPropertyChanged(nameof(SourceSwitchPrompt));
            return;
        }

        await LoadCandidatesAsync(group, _lifetimeCancellation.Token);
    }

    /// <summary>在当前页面确认清除选择并切换来源。</summary>
    [RelayCommand]
    private async Task ConfirmSourceSwitchAsync()
    {
        if (!IsInputEnabled) return;
        var group = PendingSourceGroup;
        PendingSourceGroup = null;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
        if (group is not null && _lifetimeCancellation is { IsCancellationRequested: false } lifetime)
            await LoadCandidatesAsync(group, lifetime.Token);
    }

    /// <summary>取消来源切换并保留当前选择。</summary>
    [RelayCommand]
    private void CancelSourceSwitch()
    {
        PendingSourceGroup = null;
        OnPropertyChanged(nameof(IsSourceSwitchPending));
        OnPropertyChanged(nameof(SourceSwitchPrompt));
    }

    /// <summary>显式刷新本地筛选；不会发送后端请求。</summary>
    [RelayCommand]
    private void ApplyFilter() => RefreshLocalFilter();

    /// <summary>清空本地筛选并保留当前加载集合和勾选。</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Keyword = string.Empty;
        SourceNo = string.Empty;
        ShowOnlyErrors = false;
        RefreshLocalFilter();
    }

    /// <summary>把当前筛选可见行统一设为全选或全不选；批量语义不触发父件级联。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleSelection))]
    private void ToggleAllVisible()
    {
        if (!CanChangeVisibleSelection()) return;
        var visible = GetVisibleRows();
        if (visible.Count == 0) return;
        var target = !visible.All(static item => item.IsChecked);
        RunSelectionBatch(() =>
        {
            foreach (var item in visible) item.IsChecked = target;
        });
    }

    /// <summary>逐项反转当前筛选可见行，隐藏行保持不变；批量期间抑制父件自动联动。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleSelection))]
    private void InvertVisibleSelection()
    {
        if (!CanChangeVisibleSelection()) return;
        var visible = GetVisibleRows();
        if (visible.Count == 0) return;
        RunSelectionBatch(() =>
        {
            foreach (var item in visible) item.IsChecked = !item.IsChecked;
        });
    }

    private bool CanChangeVisibleSelection() => IsInputEnabled
        && IsDetailLoaded
        && !IsLoading
        && (_workflow is null || (!_workflow.IsRecoveryInProgress
            && !_workflow.IsCreateInProgress
            && !_workflow.HasUnknownCreateResult));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        if (!IsInputEnabled || _preparationService is null) return;
        var previous = _supplierSearchCancellation;
        var request = new CancellationTokenSource();
        _supplierSearchCancellation = request;
        previous?.Cancel();
        IsSupplierSearchBusy = true;
        try
        {
            var result = await _preparationService.SearchSuppliersAsync(new PagedRequestBase
            {
                Keyword = keyword?.Trim(),
                PageIndex = 1,
                PageSize = 50,
            }, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_supplierSearchCancellation, request)) return;
            SupplierCandidates.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var supplier in result.Data.Items) SupplierCandidates.Add(supplier);
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                StatusText = result.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_supplierSearchCancellation, request))
            {
                _supplierSearchCancellation = null;
                IsSupplierSearchBusy = false;
            }
            request.Dispose();
        }
    }

    [RelayCommand]
    private void CommitPurchaseSupplier(PurchaseCandidateItemModel? item)
    {
        if (IsInputEnabled && item?.CanEditPurchaseSupplier == true) item.CommitPurchaseSupplier();
    }

    [RelayCommand]
    private void ClearPurchaseSupplier(PurchaseCandidateItemModel? item)
    {
        if (IsInputEnabled && item?.CanEditPurchaseSupplier == true) item.ClearPurchaseSupplier();
    }

    [RelayCommand]
    private void CommitDeliverySupplier(PurchaseCandidateItemModel? item)
    {
        if (IsInputEnabled && item?.CanEditDeliverySupplier == true) item.CommitDeliverySupplier();
    }

    [RelayCommand]
    private void ClearDeliverySupplier(PurchaseCandidateItemModel? item)
    {
        if (IsInputEnabled && item?.CanEditDeliverySupplier == true) item.ClearDeliverySupplier();
    }

    /// <summary>统一刷新所有行的输入许可，使编辑器与 EnterpriseGrid 填充使用同一业务门禁。</summary>
    public void SetInputEnabled(bool isEnabled)
    {
        if (!isEnabled) _supplierSearchCancellation?.Cancel();
        IsInputEnabled = isEnabled;
        foreach (var item in Items) item.IsInputEnabled = isEnabled;
    }

    /// <summary>提交当前原生编辑事务；提交失败时调用方不得读取或发送旧模型值。</summary>
    public bool TryCommitPendingEdits() => CommitPendingEdits?.Invoke() ?? true;

    /// <summary>由统一容器接入集中会话；采购准备页据此独立拥有校验和进入审核命令。</summary>
    public void AttachWorkflow(PurchaseDraftWorkflowViewModel workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (ReferenceEquals(_workflow, workflow)) return;
        if (_workflow is not null) _workflow.PropertyChanged -= OnWorkflowPropertyChanged;
        _workflow = workflow;
        _workflow.PropertyChanged += OnWorkflowPropertyChanged;
        OnPropertyChanged(nameof(Workflow));
        ValidateSelectionCommand.NotifyCanExecuteChanged();
        EnterPreviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>校验全部勾选行（包含筛选隐藏行），成功后缓存同一份有效预览供进入审核直接接纳。</summary>
    [RelayCommand(CanExecute = nameof(CanValidateSelection))]
    private async Task ValidateSelectionAsync()
    {
        if (_workflow is null) return;
        HasValidationAttempted = true;
        HasValidatedSelection = false;
        if (!TryCommitPendingEdits())
        {
            SelectionValidationText = "当前单元格输入格式无效，请修正后重新校验所选物料。";
            return;
        }

        var rows = SelectedRows;
        if (rows.Count == 0)
        {
            SelectionValidationText = "请至少选择一条待采购物料。";
            return;
        }
        var localError = rows.FirstOrDefault(static row => !row.IsAvailableForPurchase
            || row.PurchaseQty <= 0
            || row.PurchaseQty > row.RemainingQty);
        if (localError is not null)
        {
            SelectedItem = localError;
            SelectionValidationText = localError.ValidationDisplayMessage;
            return;
        }

        SelectionValidationText = string.Empty;
        SetInputEnabled(false);
        try
        {
            await _workflow.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
            {
                EntryMode = PurchaseDraftEntryModeEnum.PurchaseWorkspace,
                Rows = rows,
            });
            ApplyValidation(_workflow.Warnings.ToArray());
            HasValidatedSelection = _workflow.HasPreview && !_workflow.IsStale && !_workflow.HasBlockers;
            SelectionValidationText = BuildSelectionValidationText();
        }
        finally
        {
            SetInputEnabled(true);
        }
    }

    /// <summary>接纳刚刚验证成功的同一份预览，不重复调用服务端。</summary>
    [RelayCommand(CanExecute = nameof(CanEnterPreview))]
    private void EnterPreview() => PreviewRequested?.Invoke(this, EventArgs.Empty);

    private bool CanValidateSelection() => _workflow is not null
        && IsInputEnabled
        && !IsLoading
        && !_workflow.IsRecoveryInProgress
        && !_workflow.IsCreateInProgress
        && !_workflow.HasUnknownCreateResult
        && SelectedCount > 0;

    private bool CanEnterPreview() => HasValidatedSelection
        && IsInputEnabled
        && _workflow?.HasValidPreparation == true
        && !_workflow.IsRecoveryInProgress
        && !_workflow.IsCreateInProgress;

    /// <summary>审核刷新成功后接纳其验证快照；若仍有强提示，确认完成即可再次进入审核。</summary>
    public void AcceptCurrentPreviewValidation()
    {
        HasValidatedSelection = _workflow is { HasPreview: true, IsStale: false, HasBlockers: false };
        SelectionValidationText = BuildSelectionValidationText();
        EnterPreviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>优先展示服务端真实强确认与影响对象，避免用笼统的预览失效文案覆盖有效警告。</summary>
    private string BuildSelectionValidationText()
    {
        if (_workflow is null) return string.Empty;
        if (_workflow.HasValidPreparation) return "校验和确认已完成，可以进入采购审核。";
        if (!_workflow.IsStale && _workflow.HasUnconfirmedWarnings)
        {
            return string.Join("；", _workflow.Warnings
                .Where(static warning => warning.RequiresConfirmation && !warning.IsConfirmed)
                .Select(static warning => $"{warning.ActionText} 影响：{warning.AffectedObjectText}"));
        }
        return _workflow.CreateBlockReason;
    }

    /// <summary>重新加载当前来源完整范围；只有成功后才合并，传输失败完整保留当前集合和输入。</summary>
    public async Task<bool> RestoreCurrentScopeAsync(
        IReadOnlyList<string> completedSourceKeys,
        CancellationToken cancellationToken = default)
    {
        var request = ReplaceCancellation(ref _detailCancellation, cancellationToken);
        var sourceGroup = SelectedSourceGroup;
        IsLoading = true;
        StatusText = "正在刷新采购候选事实...";
        try
        {
            var result = await _runner.RunAsync(
                () => _api.GetCandidatesAsync(new PurchaseDraftCandidateQuery
                {
                    SourceType = sourceGroup is { IsAllSources: false } ? sourceGroup.SourceType : string.Empty,
                    SourceUid = sourceGroup is { IsAllSources: false } ? sourceGroup.SourceUid : null,
                }, request.Token),
                request.Token,
                "刷新采购候选失败");
            if (!CanAccept(_detailCancellation, request)) return false;
            if (!result.Success || result.Data is null)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Message)
                    ? "刷新采购候选未完成，当前输入已保留，可重试。"
                    : $"{result.Message} 当前输入已保留，可重试。";
                return false;
            }

            if (!ReferenceEquals(SelectedSourceGroup, sourceGroup)) return false;
            MergeRecoveredCandidates(result.Data, completedSourceKeys);
            HasValidationAttempted = false;
            HasValidatedSelection = false;
            SelectionValidationText = "候选事实已刷新，请处理标记项后重新校验。";
            return true;
        }
        finally
        {
            CompleteRequest(ref _detailCancellation, request);
        }
    }

    /// <summary>以服务端新事实替换匹配行，同时保留采购员输入；当前范围缺失行原样保留并标为不可采购。</summary>
    private void MergeRecoveredCandidates(
        IReadOnlyList<PurchaseDraftCandidateDto> candidates,
        IReadOnlyList<string> completedSourceKeys)
    {
        var candidateById = candidates.GroupBy(static item => item.Uid).ToDictionary(static group => group.Key, static group => group.First());
        var completed = completedSourceKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = Items.ToArray();
        var merged = new List<PurchaseCandidateItemModel>(current.Length + candidates.Count);
        foreach (var item in current)
        {
            var isCompleted = completed.Contains(CreateSourceKey(item.SourceType, item.SourceUid));
            if (candidateById.Remove(item.Uid, out var candidate))
            {
                var refreshed = PurchaseCandidateItemModel.MergeRefreshed(candidate, item);
                if (isCompleted) refreshed.IsChecked = false;
                merged.Add(refreshed);
                continue;
            }
            if (item.IsChecked && !isCompleted)
            {
                item.MarkOutsidePurchaseScope();
                merged.Add(item);
            }
        }
        foreach (var candidate in candidateById.Values)
        {
            var added = PurchaseCandidateItemModel.FromDto(candidate);
            added.IsChecked = false;
            merged.Add(added);
        }

        ReplaceItems(merged);
        RefreshLocalFilter();
    }

    private static string CreateSourceKey(string sourceType, Guid sourceUid) =>
        $"{sourceType.Trim().ToUpperInvariant()}|{sourceUid:N}";

    private void ReplaceItems(IEnumerable<PurchaseCandidateItemModel> items)
    {
        foreach (var item in Items)
        {
            item.ValueChanged -= OnRowValueChanged;
            item.SelectionChanged -= OnRowSelectionChanged;
        }
        Items.Clear();
        foreach (var item in items)
        {
            item.ValueChanged += OnRowValueChanged;
            item.SelectionChanged += OnRowSelectionChanged;
            Items.Add(item);
        }
    }

    /// <summary>把服务端结构化验证结果映射回稳定需求行，供只看错误和定位编辑使用。</summary>
    public void ApplyValidation(IReadOnlyList<PurchaseDraftWarningModel> warnings)
    {
        foreach (var item in Items) item.ValidationMessage = string.Empty;
        foreach (var warning in warnings.Where(static warning => warning.IsBlocker))
        {
            foreach (var item in Items.Where(item => warning.NetDemandIds.Contains(item.Uid)))
                item.ValidationMessage = string.IsNullOrWhiteSpace(item.ValidationMessage)
                    ? warning.Message
                    : $"{item.ValidationMessage}；{warning.Message}";
        }
        RefreshLocalFilter();
        SelectedItem = Items.FirstOrDefault(static item => item.HasValidationError);
    }

    /// <summary>加载完整采购候选来源分组，不自动选择或加载明细。</summary>
    private async Task LoadGroupsAsync(CancellationToken externalCancellation)
    {
        var request = ReplaceCancellation(ref _groupCancellation, externalCancellation);
        IsLoading = true;
        try
        {
            var result = await _runner.RunAsync(
                () => _api.GetCandidateSourceGroupsAsync(request.Token),
                request.Token,
                "加载待采购来源失败");
            if (!CanAccept(_groupCancellation, request)) return;

            if (!result.Success || result.Data is null)
            {
                SourceGroupStatusText = result.Message;
                StatusText = "待采购来源加载失败，右侧未加载数据。";
                return;
            }

            var total = result.Data.Sum(static item => item.CandidateCount);
            SourceGroups.Add(PurchaseCandidateSourceGroupModel.CreateAll(total));
            foreach (var group in result.Data)
                SourceGroups.Add(PurchaseCandidateSourceGroupModel.FromDto(group));

            SourceGroupStatusText = result.Data.Count == 0
                ? "当前没有待采购来源；可打开全部入口确认。"
                : $"共 {result.Data.Count} 张来源单，{total} 条待采购物料。";
            StatusText = "请选择左侧来源查看待采购物料。";
        }
        finally
        {
            CompleteRequest(ref _groupCancellation, request);
        }
    }

    /// <summary>按来源类型和 Uid 或全部入口一次完整加载候选，并拒绝过期结果回写。</summary>
    private async Task LoadCandidatesAsync(
        PurchaseCandidateSourceGroupModel group,
        CancellationToken externalCancellation)
    {
        var request = ReplaceCancellation(ref _detailCancellation, externalCancellation);
        ClearItems();
        IsDetailLoaded = false;
        SelectedSourceGroup = group;
        IsLoading = true;
        StatusText = $"正在加载 {group.DisplaySourceNo}...";
        try
        {
            var result = await _runner.RunAsync(
                () => _api.GetCandidatesAsync(new PurchaseDraftCandidateQuery
                {
                    SourceType = group.IsAllSources ? string.Empty : group.SourceType,
                    SourceUid = group.IsAllSources ? null : group.SourceUid,
                }, request.Token),
                request.Token,
                "加载待采购物料失败");
            if (!CanAccept(_detailCancellation, request)
                || !ReferenceEquals(SelectedSourceGroup, group))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }

            foreach (var dto in result.Data)
            {
                var row = PurchaseCandidateItemModel.FromDto(dto);
                row.ValueChanged += OnRowValueChanged;
                row.SelectionChanged += OnRowSelectionChanged;
                Items.Add(row);
            }
            IsDetailLoaded = true;
            RefreshLocalFilter();
        }
        finally
        {
            CompleteRequest(ref _detailCancellation, request);
        }
    }

    /// <summary>匹配当前已加载候选的关键词和来源号，不发起远程查询。</summary>
    private bool MatchesLocalFilter(object item)
    {
        if (item is not PurchaseCandidateItemModel row) return false;
        var keyword = Keyword.Trim();
        var sourceNo = SourceNo.Trim();
        return (!ShowOnlyErrors || row.HasValidationError)
            && (keyword.Length == 0
                || Contains(row.Sku, keyword)
                || Contains(row.LinkSku, keyword)
                || Contains(row.Description, keyword)
                || Contains(row.SuggestedPurchaseSupplierName, keyword)
                || Contains(row.SuggestedPurchaseSupplierCode, keyword))
            && (sourceNo.Length == 0 || Contains(row.SourceNo, sourceNo));
    }

    private static bool Contains(string? value, string filter) =>
        value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private List<PurchaseCandidateItemModel> GetVisibleRows() =>
        ItemsView.Cast<object>().OfType<PurchaseCandidateItemModel>().ToList();

    /// <summary>刷新集合视图并同步加载、筛选和选择计数。</summary>
    private void RefreshLocalFilter()
    {
        ItemsView.Refresh();
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        UpdateCountStatus();
    }

    private void UpdateCountStatus()
    {
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedCount));
        if (IsDetailLoaded && !IsLoading)
            StatusText = $"已加载 {Items.Count} 条，筛选后 {FilteredCount} 条，已选 {SelectedCount} 条。";
    }

    /// <summary>直接勾选父件时只联动同来源、明确指向该父需求且仍可采购的已加载客供件。</summary>
    private void OnRowSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not PurchaseCandidateItemModel row) return;
        if (_selectionBatchDepth > 0)
        {
            _selectionBatchChanged = true;
            return;
        }

        var linkedCount = 0;
        RunSelectionBatch(() =>
        {
            if (!IsInputEnabled || !row.IsChecked || row.IsCustomerSupplied) return;
            foreach (var related in Items.Where(item => item.IsCustomerSupplied
                         && item.IsAvailableForPurchase
                         && item.DeliveryTargetNetDemandId == row.Uid
                         && SameSource(item, row)
                         && !item.IsChecked))
            {
                related.IsChecked = true;
                linkedCount++;
            }
        }, includeExistingChange: true, linkedCountProvider: () => linkedCount);
    }

    /// <summary>合并批量选择通知，使全选、全不选和反选严格保持自身语义且只失效一次预览。</summary>
    private void RunSelectionBatch(
        Action action,
        bool includeExistingChange = false,
        Func<int>? linkedCountProvider = null)
    {
        var ownsBatch = _selectionBatchDepth == 0;
        if (ownsBatch) _selectionBatchChanged = includeExistingChange;
        _selectionBatchDepth++;
        try
        {
            action();
        }
        finally
        {
            _selectionBatchDepth--;
            if (ownsBatch && _selectionBatchDepth == 0)
                CompleteSelectionBatch(linkedCountProvider?.Invoke() ?? 0);
        }
    }

    private static bool SameSource(PurchaseCandidateItemModel left, PurchaseCandidateItemModel right) =>
        left.SourceUid == right.SourceUid
        && string.Equals(left.SourceType, right.SourceType, StringComparison.OrdinalIgnoreCase);

    /// <summary>选择集合成批变化后统一清除旧确认、刷新计数和命令；联动提示只报告真实新增数量。</summary>
    private void CompleteSelectionBatch(int linkedCount)
    {
        if (!_selectionBatchChanged) return;
        _selectionBatchChanged = false;
        foreach (var item in Items) item.ValidationMessage = string.Empty;
        HasValidatedSelection = false;
        SelectionValidationText = string.Empty;
        _workflow?.InvalidatePreparation();
        InputChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        UpdateCountStatus();
        if (linkedCount > 0)
            StatusText = $"已联动选择 {linkedCount} 条同来源关联客供件；当前已选 {SelectedCount} 条。";
        ValidateSelectionCommand.NotifyCanExecuteChanged();
        EnterPreviewCommand.NotifyCanExecuteChanged();
    }

    private void OnRowValueChanged(object? sender, EventArgs e)
    {
        if (sender is PurchaseCandidateItemModel row) row.ValidationMessage = string.Empty;
        HasValidatedSelection = false;
        SelectionValidationText = string.Empty;
        _workflow?.InvalidatePreparation();
        InputChanged?.Invoke(this, EventArgs.Empty);
        UpdateCountStatus();
        ValidateSelectionCommand.NotifyCanExecuteChanged();
        EnterPreviewCommand.NotifyCanExecuteChanged();
    }

    private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PurchaseDraftWorkflowViewModel.HasValidPreparation)
            or nameof(PurchaseDraftWorkflowViewModel.HasUnconfirmedWarnings)
            or nameof(PurchaseDraftWorkflowViewModel.IsStale)
            or nameof(PurchaseDraftWorkflowViewModel.IsCreateInProgress)
            or nameof(PurchaseDraftWorkflowViewModel.HasUnknownCreateResult)
            or nameof(PurchaseDraftWorkflowViewModel.IsRecoveryInProgress))
        {
            HasValidatedSelection &= _workflow is { HasPreview: true, IsStale: false, HasBlockers: false };
            if (HasValidationAttempted) SelectionValidationText = BuildSelectionValidationText();
            ValidateSelectionCommand.NotifyCanExecuteChanged();
            EnterPreviewCommand.NotifyCanExecuteChanged();
            ToggleAllVisibleCommand.NotifyCanExecuteChanged();
            InvertVisibleSelectionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>解除行事件并清空当前候选，防止跨来源保留选择。</summary>
    private void ClearItems()
    {
        foreach (var item in Items)
        {
            item.ValueChanged -= OnRowValueChanged;
            item.SelectionChanged -= OnRowSelectionChanged;
        }
        Items.Clear();
        ItemsView.Refresh();
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedCount));
    }

    partial void OnKeywordChanged(string value) => RefreshLocalFilter();

    partial void OnSourceNoChanged(string value) => RefreshLocalFilter();

    partial void OnShowOnlyErrorsChanged(bool value) => RefreshLocalFilter();

    private static CancellationTokenSource ReplaceCancellation(
        ref CancellationTokenSource? owner,
        CancellationToken externalCancellation)
    {
        var previous = owner;
        owner = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        previous?.Cancel();
        return owner;
    }

    private bool CanAccept(CancellationTokenSource? owner, CancellationTokenSource request) =>
        !_disposed && !request.IsCancellationRequested && ReferenceEquals(owner, request);

    private void CompleteRequest(ref CancellationTokenSource? owner, CancellationTokenSource request)
    {
        if (ReferenceEquals(owner, request))
        {
            owner = null;
            IsLoading = false;
            UpdateCountStatus();
            ValidateSelectionCommand.NotifyCanExecuteChanged();
        }
        request.Dispose();
    }

    private void CancelPendingRequests()
    {
        var groups = _groupCancellation;
        var details = _detailCancellation;
        _groupCancellation = null;
        _detailCancellation = null;
        var supplierSearch = _supplierSearchCancellation;
        _supplierSearchCancellation = null;
        groups?.Cancel();
        details?.Cancel();
        supplierSearch?.Cancel();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>对话框关闭时取消查询并阻止旧结果回写；下一次打开会重新建立生命周期。</summary>
    public void Close()
    {
        CancelPendingRequests();
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
        IsLoading = false;
    }

    /// <summary>释放选择器持有的查询资源和行事件。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_workflow is not null) _workflow.PropertyChanged -= OnWorkflowPropertyChanged;
        CommitPendingEdits = null;
        Close();
        ClearItems();
    }
}
