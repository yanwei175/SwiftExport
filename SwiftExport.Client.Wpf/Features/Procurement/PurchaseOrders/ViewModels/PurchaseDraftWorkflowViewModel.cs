using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>负责采购草稿预览、目标调整、幂等创建和逐来源结果呈现。</summary>
public sealed partial class PurchaseDraftWorkflowViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseDraftApi _api;
    private readonly IProcurementMasterDataQueryApi _masterData;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly PurchasePreparationService? _preparationService;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _purchaseSupplierSearchCancellation;
    private CancellationTokenSource? _deliverySupplierSearchCancellation;
    private CancellationTokenSource? _recoveryCancellation;
    private Func<PurchaseDraftCandidateRecoveryRequestedEventArgs, CancellationToken, Task<bool>>? _candidateRecoveryHandler;
    private PurchaseDraftPreviewResponse? _preview;
    private CreatePurchaseDraftsCommand? _pendingCreateCommand;
    private long _sessionVersion;
    private long _inputRevision;
    private long _previewSequence;
    private long _recoverySequence;
    private bool _isSessionActive;
    private bool _suppressStale;
    private bool _disposed;

    /// <summary>审核页请求返回采购准备修改。</summary>
    public event EventHandler? ReturnToPreparationRequested;
    /// <summary>有效预览刷新完成，容器据此决定继续审核或返回准备处理问题。</summary>
    public event EventHandler? PreviewRefreshCompleted;
    /// <summary>候选恢复已通过会话与修订校验，容器可安全返回采购准备。</summary>
    public event EventHandler? CandidateRecoveryCompleted;
    /// <summary>创建取得确定结果后通知容器切换结果或准备页面。</summary>
    public event EventHandler? CreationCompleted;

    public PurchaseDraftWorkflowViewModel(
        IPurchaseDraftApi api,
        IProcurementMasterDataQueryApi masterData,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        PurchasePreparationService? preparationService = null)
    {
        _api = api;
        _masterData = masterData;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _preparationService = preparationService;
        Title = "转采购草稿预览";
        RegisterBusyAwareCommand(PreviewCommand);
        RegisterBusyAwareCommand(CreateCommand);
    }

    public ObservableCollection<PurchaseDraftSelectionItemModel> Items { get; } = [];
    public ObservableCollection<PurchaseDraftTargetSelectionModel> Targets { get; } = [];
    public ObservableCollection<PurchaseDraftWarningModel> Warnings { get; } = [];
    public ObservableCollection<PurchaseDraftPreviewLineModel> PreviewLines { get; } = [];
    public ObservableCollection<PurchaseDraftDocumentReviewModel> Documents { get; } = [];
    public ObservableCollection<PurchaseDraftSkippedItemDto> SkippedItems { get; } = [];
    public ObservableCollection<PurchaseDraftCreateResultModel> Results { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> PurchaseSupplierCandidates { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> DeliverySupplierCandidates { get; } = [];

    public IReadOnlyList<NetDemandFilterOption<PurchaseDraftLineModeEnum>> LineModeOptions { get; } =
    [
        new(PurchaseDraftLineModeEnum.KeepSourceLines, "不合并"),
        new(PurchaseDraftLineModeEnum.MergeCompatibleLines, "合并"),
    ];

    public bool IsManualSelection => EntryMode == PurchaseDraftEntryModeEnum.ManualSelection;
    public bool IsSourceOneClick => EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick;
    public string EntryModeText => EntryMode switch
    {
        PurchaseDraftEntryModeEnum.ManualSelection => "净需求人工勾选",
        PurchaseDraftEntryModeEnum.SourceOneClick => "按来源一键转采购",
        PurchaseDraftEntryModeEnum.PurchaseWorkspace => "采购工作台获取净需求",
        _ => EntryMode.ToString(),
    };
    public bool HasPreview => _preview is not null;
    public bool HasBlockers => _preview?.HasBlockers == true || Warnings.Any(item => item.IsBlocker);
    public bool HasUnconfirmedWarnings => Warnings.Any(item => item.RequiresConfirmation && !item.IsConfirmed);
    public int BlockerCount => Warnings.Where(item => item.IsBlocker).Sum(item => Math.Max(1, item.AffectedCount));
    public int PendingConfirmationCount => Warnings
        .Where(item => item.RequiresConfirmation && !item.IsConfirmed)
        .Sum(item => Math.Max(1, item.AffectedCount));
    /// <summary>当前有效预览中允许创建但需后续补资料的对象数。</summary>
    public int InformationCount => Warnings
        .Where(item => !item.IsBlocker && !item.RequiresConfirmation)
        .Sum(item => Math.Max(1, item.AffectedCount));
    /// <summary>当前有效预览是否包含问题或确认提示。</summary>
    public bool HasWarnings => Warnings.Count > 0;
    /// <summary>始终可见的问题分类摘要；失效时明确旧清单已经撤销。</summary>
    public string ProblemSummaryText => IsStale
        ? "预览已失效，问题清单将在重新预览后更新。"
        : _preview is null
            ? "尚未生成有效预览。"
            : $"阻断 {BlockerCount} 项 · 待确认 {PendingConfirmationCount} 项 · 资料待补 {InformationCount} 项";
    public bool CanCreateCurrent => CanCreate();
    /// <summary>当前准备输入已通过服务端验证并完成全部强确认，可进入单据审核。</summary>
    public bool HasValidPreparation => _preview is not null
        && !IsStale
        && !HasBlockers
        && _preview.SourceGroups.All(group => group.CanCreate)
        && !HasUnconfirmedWarnings;
    public bool CanClose => !IsCreateInProgress && !HasUnknownCreateResult;
    public bool CanEditInputs => _isSessionActive && !IsCreateInProgress && !HasUnknownCreateResult && !IsRecoveryInProgress;
    public string CreateBlockReason => HasUnknownCreateResult
        ? "创建结果未知：只能按冻结负载重试。"
        : IsCreateInProgress
            ? "正在创建采购草稿，暂不可修改。"
            : ValidateItems() is { } validationError
                ? $"{validationError} 处理：修正本次计划后重新预览。"
                : IsStale
                    ? "预览已失效，请重新预览。"
                : BlockerCount > 0
                    ? $"存在 {BlockerCount} 项阻断，必须先修正并重新预览。"
                    : PendingConfirmationCount > 0
                        ? $"还有 {PendingConfirmationCount} 项待确认。"
                        : string.Empty;
    /// <summary>当前有效预览的采购项、单据数及合并前后行数；失效时不展示旧数字。</summary>
    public string PreviewSummaryText => _preview is null || IsStale
        ? "预览摘要已失效，请重新预览。"
        : $"选中物料 {Items.Count} 项 · 采购草稿 {_preview.SourceGroups.Sum(group => group.DocumentCount)} 张 · 采购行 {_preview.SourceGroups.Sum(group => group.Documents.Sum(document => document.SourceLineCount))} → {_preview.SourceGroups.Sum(group => group.Documents.Sum(document => document.PreviewLineCount))}";

    /// <summary>当前写请求结果是否未知；未知时只能按冻结负载和原幂等身份重试。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReturnToPreparationCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(CanEditInputs))]
    [NotifyPropertyChangedFor(nameof(CreateBlockReason))]
    public partial bool HasUnknownCreateResult { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualSelection))]
    [NotifyPropertyChangedFor(nameof(IsSourceOneClick))]
    [NotifyPropertyChangedFor(nameof(EntryModeText))]
    public partial PurchaseDraftEntryModeEnum EntryMode { get; private set; }

    [ObservableProperty]
    public partial Guid ExecutionRequestId { get; private set; }

    [ObservableProperty]
    public partial string SourceType { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? SourceUid { get; private set; }

    [ObservableProperty]
    public partial string SourceNo { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PurchaseDraftLineModeEnum LineMode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    [NotifyPropertyChangedFor(nameof(CreateBlockReason))]
    [NotifyPropertyChangedFor(nameof(PreviewActionText))]
    public partial bool IsStale { get; private set; } = true;

    /// <summary>审核页固定显示的恢复动作；有效或失效预览均从同一入口刷新。</summary>
    public string PreviewActionText => "刷新预览";

    /// <summary>是否正在按当前来源范围恢复采购候选；期间所有页面导航和输入均被冻结。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReturnToPreparationCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    [NotifyPropertyChangedFor(nameof(CanEditInputs))]
    public partial bool IsRecoveryInProgress { get; private set; }

    [ObservableProperty]
    public partial bool HasAppliedResults { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReturnToPreparationCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    [NotifyPropertyChangedFor(nameof(CanEditInputs))]
    [NotifyPropertyChangedFor(nameof(CreateBlockReason))]
    public partial bool IsCreateInProgress { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "尚未生成转采购预览。";

    [ObservableProperty]
    public partial string ResultSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PurchaseDraftSelectionItemModel? SelectedInputItem { get; set; }

    [ObservableProperty]
    public partial PurchaseDraftDocumentReviewModel? SelectedDocument { get; set; }

    [ObservableProperty]
    public partial PurchaseDraftPreviewLineModel? SelectedLine { get; set; }

    [ObservableProperty]
    public partial bool IsPurchaseSupplierSearchBusy { get; set; }

    [ObservableProperty]
    public partial bool IsDeliverySupplierSearchBusy { get; set; }

    /// <summary>接入当前 Dialog 的候选恢复处理器；审核流程会等待处理完成后再决定页面导航。</summary>
    public void SetCandidateRecoveryHandler(
        Func<PurchaseDraftCandidateRecoveryRequestedEventArgs, CancellationToken, Task<bool>>? handler) =>
        _candidateRecoveryHandler = handler;

    /// <summary>开始一次独立 Dialog 会话；只有这里重置累计结果并生成新的执行请求身份。</summary>
    public void BeginSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isSessionActive && !CanClose)
            throw new InvalidOperationException("当前采购写入尚未取得确定结果，不能开始新会话。");
        EndSessionCore();
        _sessionVersion++;
        _isSessionActive = true;
        _suppressStale = true;
        try
        {
            ExecutionRequestId = Guid.NewGuid();
            LineMode = PurchaseDraftLineModeEnum.MergeCompatibleLines;
        }
        finally
        {
            _suppressStale = false;
        }
        StatusText = "请选择待采购物料。";
        NotifyPreviewStateChanged();
    }

    /// <summary>更新当前会话的选择；按物料需求身份保留未变化项输入，仅替换新增、移除或事实变化项。</summary>
    public async Task UpdateSelectionAsync(PurchaseDraftPreviewOpenRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isSessionActive)
            throw new InvalidOperationException("采购 Dialog 会话尚未开始。");
        if (HasUnknownCreateResult)
            throw new InvalidOperationException("当前采购写入结果未知，只能按冻结负载重试。");

        var previousEntryMode = EntryMode;
        var previousSourceType = SourceType;
        var previousSourceUid = SourceUid;
        var previousSourceNo = SourceNo;
        var existingByDemand = Items.ToDictionary(item => item.NetDemandId);
        var selectedRows = request.Rows
            .Where(row => !IsSourceCompleted(row.SourceType, row.SourceUid))
            .GroupBy(row => row.Uid)
            .Select(group => group.First())
            .ToArray();
        var nextItems = new List<PurchaseDraftSelectionItemModel>(selectedRows.Length);
        var factsChanged = false;

        _suppressStale = true;
        try
        {
            EntryMode = request.EntryMode;
            SourceType = request.SourceType;
            SourceUid = request.SourceUid;
            SourceNo = request.SourceNo;
            foreach (var row in selectedRows)
            {
                if (existingByDemand.TryGetValue(row.Uid, out var existing) && HasSameFacts(existing, row, request.EntryMode))
                {
                    nextItems.Add(existing);
                    continue;
                }

                factsChanged |= existing is not null;
                nextItems.Add(CreateSelectionItem(row, request.EntryMode));
            }

            var selectionChanged = Items.Count != nextItems.Count
                || Items.Select(item => item.NetDemandId).Except(nextItems.Select(item => item.NetDemandId)).Any()
                || nextItems.Any(item => !existingByDemand.TryGetValue(item.NetDemandId, out var existing) || !ReferenceEquals(item, existing));
            var contextChanged = previousEntryMode != EntryMode
                || !string.Equals(previousSourceType, SourceType, StringComparison.OrdinalIgnoreCase)
                || previousSourceUid != SourceUid
                || !string.Equals(previousSourceNo, SourceNo, StringComparison.Ordinal);

            if (selectionChanged)
            {
                DetachItemEvents();
                Items.Clear();
                foreach (var item in nextItems) AddItem(item);
            }

            if (selectionChanged || contextChanged)
            {
                InvalidatePreview(factsChanged
                    ? "物料需求版本或剩余额度已变化，相关输入已按最新事实重新初始化，请重新预览。"
                    : "采购选择已变化，请重新预览。",
                    incrementRevision: true);
            }
        }
        finally
        {
            _suppressStale = false;
        }

        if (Items.Count == 0 && request.EntryMode != PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            StatusText = HasAppliedResults
                ? "所选来源均已成功创建或幂等应用，无待执行物料。"
                : "当前没有待预览的采购物料。";
            NotifyPreviewStateChanged();
            return;
        }

        if (_preview is not null && !IsStale)
            return;

        await LoadPreviewAsync(cancellationToken);
        if (factsChanged && !IsStale)
            StatusText = $"物料需求版本或剩余额度已变化，相关输入已按最新事实重新初始化；{StatusText}";
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPreview))]
    private Task PreviewAsync() => CanPreview() ? LoadPreviewAsync() : Task.CompletedTask;

    /// <summary>从只读审核返回采购准备；导航只由当前页面命令发起。</summary>
    [RelayCommand(CanExecute = nameof(CanReturnToPreparation))]
    private void ReturnToPreparation() => ReturnToPreparationRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchPurchaseSuppliersAsync(string? keyword) => SearchSuppliersAsync(
        keyword,
        PurchaseSupplierCandidates,
        isDeliverySearch: false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchDeliverySuppliersAsync(string? keyword) => SearchSuppliersAsync(
        keyword,
        DeliverySupplierCandidates,
        isDeliverySearch: true);

    /// <summary>执行可替代的有效供应商查询，仅允许当前会话的最新请求回写候选。</summary>
    private async Task SearchSuppliersAsync(
        string? keyword,
        ObservableCollection<ProcurementSupplierCandidateDto> target,
        bool isDeliverySearch)
    {
        if (!CanEditInputs) return;
        var previous = isDeliverySearch
            ? _deliverySupplierSearchCancellation
            : _purchaseSupplierSearchCancellation;
        previous?.Cancel();
        var cancellation = new CancellationTokenSource();
        if (isDeliverySearch) _deliverySupplierSearchCancellation = cancellation;
        else _purchaseSupplierSearchCancellation = cancellation;
        if (isDeliverySearch) IsDeliverySupplierSearchBusy = true;
        else IsPurchaseSupplierSearchBusy = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _masterData.SearchSuppliersAsync(new PagedRequestBase
                {
                    Keyword = keyword?.Trim(),
                    PageIndex = 1,
                    PageSize = 50,
                }, cancellation.Token),
                cancellation.Token,
                "搜索供应商失败");
            var isCurrent = ReferenceEquals(
                isDeliverySearch ? _deliverySupplierSearchCancellation : _purchaseSupplierSearchCancellation,
                cancellation);
            if (cancellation.IsCancellationRequested || !isCurrent) return;
            target.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var supplier in result.Data.Items) target.Add(supplier);
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                StatusText = result.Message;
            }
        }
        finally
        {
            var isCurrent = ReferenceEquals(
                isDeliverySearch ? _deliverySupplierSearchCancellation : _purchaseSupplierSearchCancellation,
                cancellation);
            if (isCurrent)
            {
                if (isDeliverySearch)
                {
                    _deliverySupplierSearchCancellation = null;
                    IsDeliverySupplierSearchBusy = false;
                }
                else
                {
                    _purchaseSupplierSearchCancellation = null;
                    IsPurchaseSupplierSearchBusy = false;
                }
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CommitPurchaseSupplier(PurchaseDraftSelectionItemModel? item)
    {
        if (!CanEditInputs || item is null) return;
        item.CommitPurchaseSupplier();
    }

    [RelayCommand]
    private void ClearPurchaseSupplier(PurchaseDraftSelectionItemModel? item)
    {
        if (!CanEditInputs || item is null) return;
        item.ClearPurchaseSupplier();
    }

    [RelayCommand]
    private void CommitDeliverySupplier(PurchaseDraftSelectionItemModel? item)
    {
        if (!CanEditInputs || item is null || !item.IsCustomerSupplied) return;
        item.CommitDeliverySupplier();
    }

    [RelayCommand]
    private void ClearDeliverySupplier(PurchaseDraftSelectionItemModel? item)
    {
        if (!CanEditInputs || item is null || !item.IsCustomerSupplied) return;
        item.ClearDeliverySupplier();
    }

    /// <summary>定位警告首个受影响输入项；聚合范围仍在警告卡片中完整展示。</summary>
    [RelayCommand]
    private void LocateWarning(PurchaseDraftWarningModel? warning)
    {
        if (warning?.AffectedItems.FirstOrDefault() is not { } item) return;
        SelectedInputItem = item;
    }

    private async Task LoadPreviewAsync(CancellationToken externalCancellation = default)
    {
        var validationError = ValidateItems();
        if (validationError is not null)
        {
            StatusText = validationError;
            _messageService.ShowWarning("无法生成采购预览", validationError);
            return;
        }

        if (_preview is not null && !IsStale)
            InvalidatePreview("正在刷新采购预览，创建暂不可用。", incrementRevision: false);

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _previewCancellation = cancellation;
        var sessionVersion = _sessionVersion;
        var inputRevision = _inputRevision;
        var previewSequence = ++_previewSequence;
        var request = CreatePreviewRequest();
        StatusText = "正在重新计算采购草稿预览...";
        var recoveredCandidates = false;
        var previewCompleted = false;

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await PreparePreviewAsync(request, cancellation.Token);
                if (!CanAcceptPreview(cancellation, sessionVersion, inputRevision, previewSequence))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    StatusText = BuildPreviewFailureText(result);
                    SelectedInputItem = Items.FirstOrDefault();
                    NotifyPreviewStateChanged();
                    recoveredCandidates = await RecoverCandidatesIfRequiredAsync(result.Error?.Code);
                    return;
                }

                if (result.Data.Preview.ExecutionRequestId != ExecutionRequestId)
                {
                    StatusText = "预览响应不属于当前采购会话，已拒绝接纳。";
                    return;
                }

                ApplyPreview(result.Data.Preview);
                previewCompleted = true;
            }, "正在生成采购草稿预览...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
            }

            cancellation.Dispose();
        }
        if (recoveredCandidates) CandidateRecoveryCompleted?.Invoke(this, EventArgs.Empty);
        else if (previewCompleted) PreviewRefreshCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (!CanCreate())
            return;

        var command = _pendingCreateCommand;
        if (command is null)
        {
            var preview = _preview;
            if (preview is null || IsStale)
                return;
            command = CreateCommandDto(preview);
            _pendingCreateCommand = command;
        }

        var sessionVersion = _sessionVersion;
        var recoveredCandidates = false;
        IsCreateInProgress = true;
        try
        {
            await RunWriteAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.CreateAsync(command, CancellationToken.None),
                    "创建采购草稿失败");
                if (!_isSessionActive || _disposed || sessionVersion != _sessionVersion)
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    if (IsRecoveryConflict(result.Error?.Code))
                    {
                        _pendingCreateCommand = null;
                        HasUnknownCreateResult = false;
                        StatusText = string.IsNullOrWhiteSpace(result.Message)
                            ? "采购事实已变化，请返回采购准备重新校验。"
                            : result.Message;
                        recoveredCandidates = await RecoverCandidatesIfRequiredAsync(result.Error?.Code);
                        return;
                    }
                    HasUnknownCreateResult = true;
                    StatusText = "创建结果未知。为避免重复采购，只能使用原请求身份和冻结负载重试。";
                    _messageService.ShowError("创建采购草稿失败", result.Message);
                    return;
                }

                if (result.Data.ExecutionRequestId != command.ExecutionRequestId)
                {
                    HasUnknownCreateResult = true;
                    StatusText = "服务端返回了不匹配的执行请求身份；当前结果保持未知，只能按原负载重试。";
                    _messageService.ShowError("创建采购草稿失败", StatusText);
                    return;
                }

                _pendingCreateCommand = null;
                HasUnknownCreateResult = false;
                MergeCreateResults(result.Data.Sources);
                RemoveCompletedSourceItems();
                var recoveryErrorCode = result.Data.Sources
                    .Select(static source => source.ErrorCode)
                    .FirstOrDefault(IsRecoveryConflict);
                var succeeded = Results.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.Succeeded);
                var already = Results.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.AlreadyApplied);
                var failed = Results.Count - succeeded - already;
                if (succeeded + already > 0)
                    HasAppliedResults = true;
                ResultSummary = $"成功 {succeeded}，已应用 {already}，失败 {failed}。";
                InvalidatePreview("创建结果已接纳；失败来源必须重新预览后才能再次提交。", incrementRevision: true);
                StatusText = HasAppliedResults
                    ? "采购草稿命令已执行。关闭弹窗后工作台将刷新。"
                    : "本次没有来源执行成功；请根据失败信息重新预览。";
                if (failed > 0)
                {
                    _messageService.ShowWarning("转采购部分或全部失败", ResultSummary);
                }
                else
                {
                    _messageService.ShowSuccess("采购草稿已创建", ResultSummary);
                }
                if (IsRecoveryConflict(recoveryErrorCode))
                {
                    recoveredCandidates = await RecoverCandidatesIfRequiredAsync(recoveryErrorCode);
                    return;
                }
                CreationCompleted?.Invoke(this, EventArgs.Empty);
            }, "正在创建采购草稿...");
        }
        finally
        {
            IsCreateInProgress = false;
            PreviewCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
        }
        if (recoveredCandidates) CandidateRecoveryCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>应用运行时使用组合服务；精简测试入口回退到原采购预览 API。</summary>
    private async Task<ServiceResult<PurchasePreparationResult>> PreparePreviewAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (_preparationService is not null)
            return await _preparationService.PrepareAsync(request, cancellationToken);

        var result = await _apiCallRunner.RunAsync(
            () => _api.PreviewAsync(request, cancellationToken),
            cancellationToken,
            "生成采购草稿预览失败");
        return result.Success && result.Data is not null
            ? ServiceResult<PurchasePreparationResult>.Ok(new PurchasePreparationResult { Preview = result.Data })
            : string.IsNullOrWhiteSpace(result.Error?.Code)
                ? ServiceResult<PurchasePreparationResult>.Fail(result.Message)
                : ServiceResult<PurchasePreparationResult>.Fail(
                    result.Error.Code,
                    result.Error.Message ?? result.Message,
                    result.Error.Field);
    }

    private PurchaseDraftPreviewRequest CreatePreviewRequest() => new()
    {
        ExecutionRequestId = ExecutionRequestId,
        EntryMode = EntryMode,
        LineMode = LineMode,
        SourceType = SourceType,
        SourceUid = SourceUid,
        Items = Items.Select(item => item.ToDto()).ToList(),
        Targets = Targets
            .Where(target => Items.Any(item => item.SourceUid == target.SourceUid
                && string.Equals(item.SourceType, target.SourceType, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.ToPreviewDto())
            .ToList(),
    };

    private CreatePurchaseDraftsCommand CreateCommandDto(PurchaseDraftPreviewResponse preview)
    {
        return new CreatePurchaseDraftsCommand
        {
            ExecutionRequestId = ExecutionRequestId,
            EntryMode = EntryMode,
            PreviewToken = preview.PreviewToken,
            Sources = preview.SourceGroups.Select(source => new CreatePurchaseDraftSourceCommand
            {
                SourceType = source.SourceType,
                SourceUid = source.SourceUid,
                SourcePreviewToken = source.SourcePreviewToken,
                LineMode = LineMode,
                Items = GetSourceCreateItems(source).ToList(),
                Targets = Targets
                    .Where(target => target.SourceUid == source.SourceUid && string.Equals(target.SourceType, source.SourceType, StringComparison.OrdinalIgnoreCase))
                    .Select(target => target.ToCreateDto())
                    .ToList(),
                ConfirmedWarningCodes = Warnings
                    .Where(warning => warning.RequiresConfirmation
                        && warning.IsConfirmed
                        && (warning.SourceUid is null || warning.SourceUid == source.SourceUid))
                    .Select(warning => warning.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            }).ToList(),
        };
    }

    /// <summary>建立新选择项；只有新增项或服务端事实变化项才生成新的命令项身份。</summary>
    private static PurchaseDraftSelectionItemModel CreateSelectionItem(
        PurchaseCandidateItemModel row,
        PurchaseDraftEntryModeEnum entryMode)
    {
        var item = new PurchaseDraftSelectionItemModel
        {
            CommandItemId = Guid.NewGuid(),
            NetDemandId = row.Uid,
            ExpectedVersion = row.Version,
            ExpectedRemainingQty = row.RemainingQty,
            PurchaseQty = row.PurchaseQty,
            UnitPrice = row.UnitPrice,
            TaxPrice = row.TaxPrice,
            PlannedDeliveryDate = row.PlannedDeliveryDate,
            CustomerId = row.CustomerId,
            CustomerCode = row.CustomerCode,
            CustomerName = row.CustomerName,
            SourceType = row.SourceType,
            SourceUid = row.SourceUid,
            SourceNo = row.SourceNo,
            Sku = row.Sku,
            LinkSku = row.LinkSku,
            IsCustomerSupplied = row.IsCustomerSupplied,
            SuggestedPurchaseSupplierId = row.SupplierId == Guid.Empty ? null : row.SupplierId,
            SuggestedPurchaseSupplierCode = row.PurchaseSupplierCode,
            SuggestedPurchaseSupplierName = row.PurchaseSupplierName,
            SuggestedDeliverySupplierId = row.DeliveryTargetSupplierId,
            SuggestedDeliverySupplierCode = row.DeliverySupplierCode,
            SuggestedDeliverySupplierName = row.DeliverySupplierName,
            SuggestedDeliverySupplierHint = row.SuggestedDeliverySupplierHint,
            ParentActualPurchaseSuppliers = row.ParentActualPurchaseSuppliers,
            IsQuantityReadOnly = entryMode != PurchaseDraftEntryModeEnum.ManualSelection,
        };
        item.InitializeSupplierSelections();
        return item;
    }

    /// <summary>比较会参与预览和幂等重放的事实；任一变化都必须换新命令项并重新预览。</summary>
    private static bool HasSameFacts(
        PurchaseDraftSelectionItemModel item,
        PurchaseCandidateItemModel row,
        PurchaseDraftEntryModeEnum entryMode) =>
        item.NetDemandId == row.Uid
        && item.ExpectedVersion == row.Version
        && item.ExpectedRemainingQty == row.RemainingQty
        && item.SupplierId == row.SupplierId
        && item.DeliveryTargetSupplierId == row.DeliveryTargetSupplierId
        && item.PurchaseQty == row.PurchaseQty
        && item.UnitPrice == row.UnitPrice
        && item.TaxPrice == row.TaxPrice
        && item.PlannedDeliveryDate == row.PlannedDeliveryDate
        && string.Equals(item.SuggestedPurchaseSupplierCode, row.PurchaseSupplierCode, StringComparison.Ordinal)
        && string.Equals(item.SuggestedPurchaseSupplierName, row.PurchaseSupplierName, StringComparison.Ordinal)
        && string.Equals(item.SuggestedDeliverySupplierCode, row.DeliverySupplierCode, StringComparison.Ordinal)
        && string.Equals(item.SuggestedDeliverySupplierName, row.DeliverySupplierName, StringComparison.Ordinal)
        && string.Equals(item.SuggestedDeliverySupplierHint, row.SuggestedDeliverySupplierHint, StringComparison.Ordinal)
        && item.SourceUid == row.SourceUid
        && string.Equals(item.SourceType, row.SourceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(item.SourceNo, row.SourceNo, StringComparison.Ordinal)
        && string.Equals(item.Sku, row.Sku, StringComparison.Ordinal)
        && string.Equals(item.LinkSku, row.LinkSku, StringComparison.Ordinal)
        && item.IsCustomerSupplied == row.IsCustomerSupplied
        && HaveSameSupplierFacts(item.ParentActualPurchaseSuppliers, row.ParentActualPurchaseSuppliers)
        && item.IsQuantityReadOnly == (entryMode != PurchaseDraftEntryModeEnum.ManualSelection);

    private static bool HaveSameSupplierFacts(
        IReadOnlyList<PurchaseDraftActualSupplierFactDto> left,
        IReadOnlyList<PurchaseDraftActualSupplierFactDto> right) => left
        .OrderBy(fact => fact.SupplierId)
        .Select(fact => (fact.SupplierId, fact.SupplierCode, fact.SupplierName, fact.IsActive, PurchaseOrders: NormalizePurchaseOrderNos(fact.PurchaseOrderNos)))
        .SequenceEqual(right.OrderBy(fact => fact.SupplierId)
            .Select(fact => (fact.SupplierId, fact.SupplierCode, fact.SupplierName, fact.IsActive, PurchaseOrders: NormalizePurchaseOrderNos(fact.PurchaseOrderNos))));

    /// <summary>规范化数据库聚合的采购单号顺序，避免等价事实因 STRING_AGG 顺序变化被误判为新事实。</summary>
    private static string NormalizePurchaseOrderNos(string? value) => string.Join(",",
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal));

    /// <summary>仅接纳属于当前会话、当前输入修订和最新请求的预览响应。</summary>
    private bool CanAcceptPreview(
        CancellationTokenSource cancellation,
        long sessionVersion,
        long inputRevision,
        long previewSequence) =>
        _isSessionActive
        && !_disposed
        && !cancellation.IsCancellationRequested
        && ReferenceEquals(_previewCancellation, cancellation)
        && sessionVersion == _sessionVersion
        && inputRevision == _inputRevision
        && previewSequence == _previewSequence;

    /// <summary>合并逐来源执行结果；后续成功或已应用会替换同来源先前失败，但不清除其他累计结果。</summary>
    private void MergeCreateResults(IEnumerable<CreatePurchaseDraftSourceResult> sources)
    {
        foreach (var source in sources)
        {
            var prior = Results.FirstOrDefault(item => SameSource(item.SourceType, item.SourceUid, source.SourceType, source.SourceUid));
            var sourceNo = prior?.SourceNo
                ?? Items.FirstOrDefault(item => SameSource(item.SourceType, item.SourceUid, source.SourceType, source.SourceUid))?.SourceNo
                ?? _preview?.SourceGroups.FirstOrDefault(group => SameSource(group.SourceType, group.SourceUid, source.SourceType, source.SourceUid))?.SourceNo
                ?? string.Empty;
            var model = PurchaseDraftCreateResultModel.FromDto(source, sourceNo);
            if (prior is null)
            {
                Results.Add(model);
                continue;
            }

            Results[Results.IndexOf(prior)] = model;
        }
    }

    /// <summary>成功来源按后端事务粒度锁定，从后续预览和创建集合中移除，防止再次提交。</summary>
    private void RemoveCompletedSourceItems()
    {
        var retained = Items.Where(item => !IsSourceCompleted(item.SourceType, item.SourceUid)).ToArray();
        if (retained.Length == Items.Count)
            return;

        DetachItemEvents();
        Items.Clear();
        foreach (var item in retained) AddItem(item);
    }

    private bool IsSourceCompleted(string sourceType, Guid sourceUid) => Results.Any(item =>
        SameSource(item.SourceType, item.SourceUid, sourceType, sourceUid)
        && item.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied);

    private static bool SameSource(string leftType, Guid leftUid, string rightType, Guid rightUid) =>
        leftUid == rightUid && string.Equals(leftType, rightType, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<CreatePurchaseDraftItemCommand> GetSourceCreateItems(PurchaseDraftPreviewSourceGroupDto source)
    {
        return Items
            .Where(item => item.SourceUid == source.SourceUid && string.Equals(item.SourceType, source.SourceType, StringComparison.OrdinalIgnoreCase))
            .Select(item => new CreatePurchaseDraftItemCommand
            {
                CommandItemId = item.CommandItemId,
                NetDemandId = item.NetDemandId,
                ExpectedVersion = item.ExpectedVersion,
                ExpectedRemainingQty = item.ExpectedRemainingQty,
                PurchaseQty = item.PurchaseQty,
                SupplierId = item.SupplierId,
                DeliveryTargetSupplierId = item.DeliveryTargetSupplierId,
                UnitPrice = item.UnitPrice,
                TaxPrice = item.TaxPrice,
                PlannedDeliveryDate = item.PlannedDeliveryDate,
            });
    }

    private void ApplyPreview(PurchaseDraftPreviewResponse response)
    {
        _suppressStale = true;
        try
        {
            _preview = response;
            LineMode = response.LineMode;
            if (IsSourceOneClick)
            {
                RebuildOneClickItems(response);
            }
            RebuildTargets(response);
            RebuildWarnings(response);
            RebuildLines(response);
            RebuildDocuments(response);
            SkippedItems.Clear();
            foreach (var skipped in response.SkippedItems)
            {
                SkippedItems.Add(skipped);
            }
            IsStale = false;
            StatusText = response.HasBlockers
                ? "预览存在阻断项，不能创建采购草稿。"
                : response.RequiresConfirmation
                    ? "请确认全部强提示后再创建采购草稿。"
                    : $"预览完成：{response.SourceGroups.Sum(group => group.DocumentCount)} 张采购草稿。";
        }
        finally
        {
            _suppressStale = false;
        }

        NotifyPreviewStateChanged();
    }

    private void RebuildOneClickItems(PurchaseDraftPreviewResponse response)
    {
        DetachItemEvents();
        Items.Clear();
        foreach (var entry in response.SourceGroups
                     .SelectMany(group => group.Documents.SelectMany(document => document.Lines.SelectMany(line =>
                         line.Contributions.Select(contribution => new { group, document, line, contribution }))))
                     .GroupBy(item => item.contribution.CommandItemId)
                     .Select(group => group.First()))
        {
            var item = new PurchaseDraftSelectionItemModel
            {
                CommandItemId = entry.contribution.CommandItemId,
                NetDemandId = entry.contribution.NetDemandId,
                ExpectedVersion = entry.contribution.NetDemandVersion,
                ExpectedRemainingQty = entry.contribution.ExpectedRemainingQty,
                PurchaseQty = entry.contribution.PurchaseQty,
                SourceType = entry.group.SourceType,
                SourceUid = entry.group.SourceUid,
                SourceNo = entry.group.SourceNo,
                Sku = entry.line.Sku,
                LinkSku = entry.line.LinkSku,
                IsCustomerSupplied = entry.line.IsCustomerSupplied,
                SuggestedPurchaseSupplierId = entry.contribution.SupplierId,
                SuggestedPurchaseSupplierCode = entry.document.SupplierCode,
                SuggestedPurchaseSupplierName = entry.document.SupplierName,
                SuggestedDeliverySupplierId = entry.contribution.DeliveryTargetSupplierId,
                SuggestedDeliverySupplierCode = entry.line.DeliveryTargetSupplierCode,
                SuggestedDeliverySupplierName = entry.line.DeliveryTargetSupplierName,
                IsQuantityReadOnly = true,
            };
            item.InitializeSupplierSelections();
            AddItem(item);
        }
    }

    private void RebuildTargets(PurchaseDraftPreviewResponse response)
    {
        DetachTargetEvents();
        Targets.Clear();
        foreach (var document in response.SourceGroups.SelectMany(group => group.Documents))
        {
            var choices = new List<PurchaseDraftTargetChoiceModel>
            {
                new()
                {
                    TargetMode = PurchaseDraftTargetModeEnum.CreateNew,
                    DisplayText = "新建采购草稿",
                },
            };
            if (IsManualSelection)
            {
                choices.AddRange(document.SafeAppendCandidates.Select(candidate => new PurchaseDraftTargetChoiceModel
                {
                    TargetMode = PurchaseDraftTargetModeEnum.AppendExisting,
                    ExistingPurchaseOrderId = candidate.PurchaseOrderId,
                    ExpectedPurchaseOrderVersion = candidate.Version,
                    DisplayText = $"追加到 {candidate.OrderNo}",
                }));
            }
            var target = new PurchaseDraftTargetSelectionModel
            {
                SourceType = document.SourceType,
                SourceUid = document.SourceUid,
                SourceNo = document.SourceNo,
                SupplierId = document.SupplierId,
                SupplierName = $"{document.SupplierCode} {document.SupplierName}".Trim(),
                Choices = choices,
            };
            var selected = choices.FirstOrDefault(choice => choice.TargetMode == document.TargetMode
                && choice.ExistingPurchaseOrderId == document.ExistingPurchaseOrderId) ?? choices[0];
            target.SetInitialChoice(selected);
            target.ValueChanged += OnPreviewInputChanged;
            Targets.Add(target);
        }
    }

    private void RebuildWarnings(PurchaseDraftPreviewResponse response)
    {
        DetachWarningEvents();
        Warnings.Clear();
        var warnings = response.Warnings
            .Concat(response.SourceGroups.SelectMany(group => group.Warnings))
            .GroupBy(item => $"{item.SourceType}|{item.SourceUid}|{item.Code}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToList());
        foreach (var warningDtos in warnings)
        {
            var warning = PurchaseDraftWarningModel.FromDtos(warningDtos, Items.ToArray());
            warning.ConfirmationChanged += OnWarningConfirmationChanged;
            Warnings.Add(warning);
        }
    }

    private void RebuildLines(PurchaseDraftPreviewResponse response)
    {
        PreviewLines.Clear();
        foreach (var source in response.SourceGroups)
        {
            foreach (var document in source.Documents)
            {
                foreach (var line in document.Lines)
                {
                    var model = line.Adapt<PurchaseDraftPreviewLineModel>();
                    model.SourceType = source.SourceType ?? string.Empty;
                    model.SourceUid = source.SourceUid;
                    model.SourceNo = source.SourceNo ?? string.Empty;
                    model.SupplierId = document.SupplierId;
                    model.SupplierCode = document.SupplierCode ?? string.Empty;
                    model.SupplierName = document.SupplierName ?? string.Empty;
                    PreviewLines.Add(model);
                }
            }
        }
    }

    private void AddItem(PurchaseDraftSelectionItemModel item)
    {
        item.ValueChanged += OnPreviewInputChanged;
        Items.Add(item);
    }

    private void OnPreviewInputChanged(object? sender, EventArgs e) => MarkStale();

    private void OnWarningConfirmationChanged(object? sender, EventArgs e)
    {
        NotifyPreviewStateChanged();
    }

    partial void OnLineModeChanged(PurchaseDraftLineModeEnum value) => MarkStale();

    private void MarkStale()
    {
        if (_suppressStale || !_isSessionActive || HasUnknownCreateResult)
        {
            return;
        }

        InvalidatePreview("预览参数已修改，必须重新预览后才能创建采购草稿。", incrementRevision: true);
    }

    /// <summary>候选页任一采购输入变化时撤销旧验证和预览接纳资格。</summary>
    public void InvalidatePreparation() => MarkStale();

    /// <summary>按服务端文档建立显式主从审核模型，单头仅接纳后端可信只读快照。</summary>
    private void RebuildDocuments(PurchaseDraftPreviewResponse response)
    {
        var selectedDocumentKey = SelectedDocument?.DocumentKey;
        var selectedLineKey = SelectedLine is null
            ? string.Empty
            : BuildLineSelectionKey(SelectedLine);
        Documents.Clear();
        foreach (var source in response.SourceGroups)
        {
            foreach (var document in source.Documents)
            {
                var target = Targets.First(item => item.SourceUid == document.SourceUid
                    && string.Equals(item.SourceType, document.SourceType, StringComparison.OrdinalIgnoreCase)
                    && item.SupplierId == document.SupplierId);
                var lines = PreviewLines.Where(line => line.SourceUid == document.SourceUid
                    && string.Equals(line.SourceType, document.SourceType, StringComparison.OrdinalIgnoreCase)
                    && line.SupplierId == document.SupplierId).ToArray();
                Documents.Add(new PurchaseDraftDocumentReviewModel
                {
                    SourceType = document.SourceType,
                    SourceUid = document.SourceUid,
                    SourceNo = document.SourceNo,
                    SupplierId = document.SupplierId,
                    SupplierDisplay = $"{document.SupplierName}".Trim(),
                    CustomerId = document.CustomerId,
                    CustomerDisplay = $"{document.CustomerName}".Trim(),
                    Target = target,
                    Header = document.Header.Adapt<PurchaseDraftHeaderReviewModel>(),
                    Lines = lines,
                });
            }
        }
        SelectedDocument = Documents.FirstOrDefault(document => document.DocumentKey == selectedDocumentKey)
            ?? Documents.FirstOrDefault();
        SelectedLine = SelectedDocument?.Lines.FirstOrDefault(line => BuildLineSelectionKey(line) == selectedLineKey)
            ?? SelectedDocument?.Lines.FirstOrDefault();
    }

    /// <summary>用采购行及其来源贡献身份恢复刷新前选中行，不依赖集合实例或显示顺序。</summary>
    private static string BuildLineSelectionKey(PurchaseDraftPreviewLineModel line) =>
        $"{line.Sku}|{line.SupplierSku}|{string.Join(",", line.Contributions.Select(static item => item.NetDemandId).OrderBy(static id => id))}";

    partial void OnSelectedDocumentChanged(PurchaseDraftDocumentReviewModel? value) =>
        SelectedLine = value?.Lines.FirstOrDefault();

    /// <summary>立即失效当前预览、取消在途查询并清除旧警告确认；累计执行结果保持不变。</summary>
    private void InvalidatePreview(string statusText, bool incrementRevision)
    {
        if (incrementRevision)
        {
            _inputRevision++;
            _recoveryCancellation?.Cancel();
        }
        _previewCancellation?.Cancel();
        _preview = null;
        IsStale = true;
        DetachWarningEvents();
        Warnings.Clear();
        StatusText = statusText;
        NotifyPreviewStateChanged();
    }

    private string? ValidateItems()
    {
        if (EntryMode == PurchaseDraftEntryModeEnum.ManualSelection && Items.Count == 0)
        {
            return "人工转采购至少需要一条计划。";
        }
        if (IsSourceOneClick && SourceUid is null)
        {
            return "按来源一键转采购缺少来源 Uid。";
        }
        return Items.Select(item => item.QuantityError).FirstOrDefault(error => error is not null);
    }

    /// <summary>按结构化错误代码给出预览失败后的恢复动作，不依赖服务端消息文本分类。</summary>
    private static string BuildPreviewFailureText<T>(ServiceResult<T> result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? "采购预览未完成。" : result.Message;
        return result.Error?.Code switch
        {
            PurchaseDraftErrorCodes.PreviewExpired
                or PurchaseDraftErrorCodes.PreviewFactChanged
                or PurchaseDraftErrorCodes.PreviewTokenMismatch
                or PurchaseDraftErrorCodes.NetDemandNotFound => $"{message} 处理：正在恢复采购候选；若该物料已失效，请取消勾选。",
            PurchaseDraftErrorCodes.SourceNotFound => $"{message} 处理：正在恢复来源范围；若来源已关闭，请取消对应选择。",
            _ => $"{message} 处理：核对本次计划后重新预览。",
        };
    }

    private static bool IsRecoveryConflict(string? errorCode) => errorCode is
        PurchaseDraftErrorCodes.PreviewExpired
        or PurchaseDraftErrorCodes.PreviewFactChanged
        or PurchaseDraftErrorCodes.PreviewTokenMismatch
        or PurchaseDraftErrorCodes.NetDemandNotFound
        or PurchaseDraftErrorCodes.SourceNotFound;

    /// <summary>串行等待当前来源范围恢复；会话、输入修订或请求身份变化后拒绝旧恢复结果。</summary>
    private async Task<bool> RecoverCandidatesIfRequiredAsync(string? errorCode)
    {
        if (!IsRecoveryConflict(errorCode) || _candidateRecoveryHandler is null) return false;
        _recoveryCancellation?.Cancel();
        _recoveryCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _recoveryCancellation = cancellation;
        var sessionVersion = _sessionVersion;
        var inputRevision = _inputRevision;
        var recoverySequence = ++_recoverySequence;
        IsRecoveryInProgress = true;
        var accepted = false;
        try
        {
            var recovered = await _candidateRecoveryHandler(new PurchaseDraftCandidateRecoveryRequestedEventArgs
            {
                ErrorCode = errorCode ?? string.Empty,
                CompletedSourceKeys = Results
                    .Where(static item => item.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied)
                    .Select(static item => CreateSourceKey(item.SourceType, item.SourceUid))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            }, cancellation.Token);
            accepted = recovered
                && _isSessionActive
                && !_disposed
                && !cancellation.IsCancellationRequested
                && ReferenceEquals(_recoveryCancellation, cancellation)
                && sessionVersion == _sessionVersion
                && inputRevision == _inputRevision
                && recoverySequence == _recoverySequence;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            accepted = false;
        }
        finally
        {
            if (ReferenceEquals(_recoveryCancellation, cancellation))
            {
                _recoveryCancellation = null;
                IsRecoveryInProgress = false;
            }
            cancellation.Dispose();
        }
        return accepted;
    }

    private static string CreateSourceKey(string sourceType, Guid sourceUid) =>
        $"{sourceType.Trim().ToUpperInvariant()}|{sourceUid:N}";

    private bool CanPreview() => _isSessionActive
        && !IsCreateInProgress
        && !HasUnknownCreateResult
        && !IsRecoveryInProgress
        && IsNotBusy
        && ValidateItems() is null;

    private bool CanReturnToPreparation() => IsNotBusy && !IsCreateInProgress && !HasUnknownCreateResult && !IsRecoveryInProgress;

    private bool CanCreate()
    {
        if (!_isSessionActive || IsCreateInProgress || IsRecoveryInProgress || !CanWrite || !IsNotBusy)
            return false;
        if (HasUnknownCreateResult)
            return _pendingCreateCommand is not null;
        return _preview is not null
            && !IsStale
            && !HasBlockers
            && _preview.SourceGroups.All(group => group.CanCreate)
            && !HasUnconfirmedWarnings;
    }

    private void NotifyPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(HasUnconfirmedWarnings));
        OnPropertyChanged(nameof(BlockerCount));
        OnPropertyChanged(nameof(PendingConfirmationCount));
        OnPropertyChanged(nameof(InformationCount));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(ProblemSummaryText));
        OnPropertyChanged(nameof(CanCreateCurrent));
        OnPropertyChanged(nameof(HasValidPreparation));
        OnPropertyChanged(nameof(CanEditInputs));
        OnPropertyChanged(nameof(CreateBlockReason));
        OnPropertyChanged(nameof(PreviewSummaryText));
        PreviewCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        ReturnToPreparationCommand.NotifyCanExecuteChanged();
    }

    /// <summary>结束当前会话并清空本次累计状态；调用方必须先读取 Dialog 结果。</summary>
    private void EndSessionCore()
    {
        _isSessionActive = false;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _recoveryCancellation?.Cancel();
        _recoveryCancellation?.Dispose();
        _recoveryCancellation = null;
        IsRecoveryInProgress = false;
        CancelSupplierSearches();
        DetachItemEvents();
        DetachTargetEvents();
        DetachWarningEvents();
        Items.Clear();
        Targets.Clear();
        Warnings.Clear();
        PreviewLines.Clear();
        SkippedItems.Clear();
        Results.Clear();
        PurchaseSupplierCandidates.Clear();
        DeliverySupplierCandidates.Clear();
        SelectedInputItem = null;
        SelectedDocument = null;
        SelectedLine = null;
        _preview = null;
        _pendingCreateCommand = null;
        HasUnknownCreateResult = false;
        HasAppliedResults = false;
        IsCreateInProgress = false;
        _inputRevision = 0;
        _previewSequence = 0;
        _recoverySequence = 0;
        ResultSummary = string.Empty;
        StatusText = "尚未生成转采购预览。";
        IsStale = true;
        NotifyPreviewStateChanged();
    }

    /// <summary>结束页面级供应商查询，防止关闭会话后旧结果回写候选集合。</summary>
    private void CancelSupplierSearches()
    {
        _purchaseSupplierSearchCancellation?.Cancel();
        _purchaseSupplierSearchCancellation = null;
        _deliverySupplierSearchCancellation?.Cancel();
        _deliverySupplierSearchCancellation = null;
        IsPurchaseSupplierSearchBusy = false;
        IsDeliverySupplierSearchBusy = false;
    }

    /// <summary>在调用方读取结果后结束本次 Dialog 会话，并拒绝所有迟到查询回写。</summary>
    public void Close()
    {
        _sessionVersion++;
        EndSessionCore();
    }

    private void DetachItemEvents()
    {
        foreach (var item in Items)
        {
            item.ValueChanged -= OnPreviewInputChanged;
        }
    }

    private void DetachTargetEvents()
    {
        foreach (var target in Targets)
        {
            target.ValueChanged -= OnPreviewInputChanged;
        }
    }

    private void DetachWarningEvents()
    {
        foreach (var warning in Warnings)
        {
            warning.ConfirmationChanged -= OnWarningConfirmationChanged;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Close();
    }
}
