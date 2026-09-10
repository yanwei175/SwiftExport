using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>负责采购草稿预览、目标调整、幂等创建和逐来源结果呈现。</summary>
public sealed partial class PurchaseDraftWorkflowViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseDraftApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _previewCancellation;
    private PurchaseDraftPreviewResponse? _preview;
    private CreatePurchaseDraftsCommand? _pendingCreateCommand;
    private long _sessionVersion;
    private long _inputRevision;
    private long _previewSequence;
    private bool _isSessionActive;
    private bool _suppressStale;
    private bool _disposed;

    public PurchaseDraftWorkflowViewModel(
        IPurchaseDraftApi api,
        IApiCallRunner apiCallRunner,
        IMessageService messageService)
    {
        _api = api;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        Title = "转采购草稿预览";
        RegisterBusyAwareCommand(PreviewCommand);
        RegisterBusyAwareCommand(CreateCommand);
    }

    public ObservableCollection<PurchaseDraftSelectionItemModel> Items { get; } = [];
    public ObservableCollection<PurchaseDraftTargetSelectionModel> Targets { get; } = [];
    public ObservableCollection<PurchaseDraftWarningModel> Warnings { get; } = [];
    public ObservableCollection<PurchaseDraftPreviewLineModel> PreviewLines { get; } = [];
    public ObservableCollection<PurchaseDraftSkippedItemDto> SkippedItems { get; } = [];
    public ObservableCollection<PurchaseDraftCreateResultModel> Results { get; } = [];

    public IReadOnlyList<NetDemandFilterOption<PurchaseDraftLineModeEnum>> LineModeOptions { get; } =
    [
        new(PurchaseDraftLineModeEnum.KeepSourceLines, "保持来源明细"),
        new(PurchaseDraftLineModeEnum.MergeCompatibleLines, "合并兼容采购行"),
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
    public bool CanCreateCurrent => CanCreate();
    public bool CanClose => !IsCreateInProgress && !HasUnknownCreateResult;
    /// <summary>当前有效预览的采购项、单据数及合并前后行数；失效时不展示旧数字。</summary>
    public string PreviewSummaryText => _preview is null || IsStale
        ? "预览摘要已失效，请重新预览。"
        : $"选中物料 {Items.Count} 项 · 采购草稿 {_preview.SourceGroups.Sum(group => group.DocumentCount)} 张 · 采购行 {_preview.SourceGroups.Sum(group => group.Documents.Sum(document => document.SourceLineCount))} → {_preview.SourceGroups.Sum(group => group.Documents.Sum(document => document.PreviewLineCount))}";

    /// <summary>当前写请求结果是否未知；未知时只能按冻结负载和原幂等身份重试。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
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
    public partial bool IsStale { get; private set; } = true;

    [ObservableProperty]
    public partial bool HasAppliedResults { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(CanCreateCurrent))]
    public partial bool IsCreateInProgress { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "尚未生成转采购预览。";

    [ObservableProperty]
    public partial string ResultSummary { get; private set; } = string.Empty;

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

    private async Task LoadPreviewAsync(CancellationToken externalCancellation = default)
    {
        var validationError = ValidateItems();
        if (validationError is not null)
        {
            StatusText = validationError;
            _messageService.ShowWarning("无法生成采购预览", validationError);
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _previewCancellation = cancellation;
        var sessionVersion = _sessionVersion;
        var inputRevision = _inputRevision;
        var previewSequence = ++_previewSequence;
        var request = CreatePreviewRequest();
        StatusText = "正在重新计算采购草稿预览...";

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.PreviewAsync(request, cancellation.Token),
                    cancellation.Token,
                    "生成采购草稿预览失败");
                if (!CanAcceptPreview(cancellation, sessionVersion, inputRevision, previewSequence))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    StatusText = result.Message;
                    return;
                }

                if (result.Data.ExecutionRequestId != ExecutionRequestId)
                {
                    StatusText = "预览响应不属于当前采购会话，已拒绝接纳。";
                    return;
                }

                ApplyPreview(result.Data);
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
            }, "正在创建采购草稿...");
        }
        finally
        {
            IsCreateInProgress = false;
            PreviewCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
        }
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
        PurchaseDraftEntryModeEnum entryMode) => new()
    {
        CommandItemId = Guid.NewGuid(),
        NetDemandId = row.Uid,
        ExpectedVersion = row.Version,
        ExpectedRemainingQty = row.RemainingQty,
        PurchaseQty = row.RemainingQty,
        SupplierId = row.SuggestedPurchaseSupplierId ?? Guid.Empty,
        DeliveryTargetSupplierId = row.SuggestedDeliverySupplierId,
        SourceType = row.SourceType,
        SourceUid = row.SourceUid,
        SourceNo = row.SourceNo,
        Sku = row.Sku,
        IsQuantityReadOnly = entryMode != PurchaseDraftEntryModeEnum.ManualSelection,
    };

    /// <summary>比较会参与预览和幂等重放的事实；任一变化都必须换新命令项并重新预览。</summary>
    private static bool HasSameFacts(
        PurchaseDraftSelectionItemModel item,
        PurchaseCandidateItemModel row,
        PurchaseDraftEntryModeEnum entryMode) =>
        item.NetDemandId == row.Uid
        && item.ExpectedVersion == row.Version
        && item.ExpectedRemainingQty == row.RemainingQty
        && item.SupplierId == (row.SuggestedPurchaseSupplierId ?? Guid.Empty)
        && item.DeliveryTargetSupplierId == row.SuggestedDeliverySupplierId
        && item.SourceUid == row.SourceUid
        && string.Equals(item.SourceType, row.SourceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(item.SourceNo, row.SourceNo, StringComparison.Ordinal)
        && string.Equals(item.Sku, row.Sku, StringComparison.Ordinal)
        && item.IsQuantityReadOnly == (entryMode != PurchaseDraftEntryModeEnum.ManualSelection);

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
            var model = PurchaseDraftCreateResultModel.FromDto(source);
            var prior = Results.FirstOrDefault(item => SameSource(item.SourceType, item.SourceUid, source.SourceType, source.SourceUid));
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
        var contributions = source.Documents
            .SelectMany(document => document.Lines)
            .SelectMany(line => line.Contributions)
            .GroupBy(item => item.CommandItemId)
            .Select(group => group.First())
            .ToArray();
        if (contributions.Length > 0)
        {
            return contributions.Select(item => new CreatePurchaseDraftItemCommand
            {
                CommandItemId = item.CommandItemId,
                NetDemandId = item.NetDemandId,
                ExpectedVersion = item.NetDemandVersion,
                ExpectedRemainingQty = item.ExpectedRemainingQty,
                PurchaseQty = item.PurchaseQty,
                SupplierId = item.SupplierId,
                DeliveryTargetSupplierId = item.DeliveryTargetSupplierId,
            });
        }

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
                         line.Contributions.Select(contribution => new { group, line, contribution }))))
                     .GroupBy(item => item.contribution.CommandItemId)
                     .Select(group => group.First()))
        {
            AddItem(new PurchaseDraftSelectionItemModel
            {
                CommandItemId = entry.contribution.CommandItemId,
                NetDemandId = entry.contribution.NetDemandId,
                ExpectedVersion = entry.contribution.NetDemandVersion,
                ExpectedRemainingQty = entry.contribution.ExpectedRemainingQty,
                PurchaseQty = entry.contribution.PurchaseQty,
                SupplierId = entry.contribution.SupplierId,
                DeliveryTargetSupplierId = entry.contribution.DeliveryTargetSupplierId,
                SourceType = entry.group.SourceType,
                SourceUid = entry.group.SourceUid,
                SourceNo = entry.group.SourceNo,
                Sku = entry.line.Sku,
                IsQuantityReadOnly = true,
            });
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
            .GroupBy(item => $"{item.SourceType}|{item.SourceUid}|{item.Code}|{item.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        foreach (var warningDto in warnings)
        {
            var warning = PurchaseDraftWarningModel.FromDto(warningDto);
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
                    model.SourceNo = source.SourceNo ?? string.Empty;
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

    /// <summary>立即失效当前预览、取消在途查询并清除旧警告确认；累计执行结果保持不变。</summary>
    private void InvalidatePreview(string statusText, bool incrementRevision)
    {
        if (incrementRevision)
            _inputRevision++;
        _previewCancellation?.Cancel();
        _preview = null;
        IsStale = true;
        foreach (var warning in Warnings.Where(item => item.IsConfirmed))
            warning.IsConfirmed = false;
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

    private bool CanPreview() => _isSessionActive
        && !IsCreateInProgress
        && !HasUnknownCreateResult
        && ValidateItems() is null;

    private bool CanCreate()
    {
        if (!_isSessionActive || IsCreateInProgress || !CanWrite || !IsNotBusy)
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
        OnPropertyChanged(nameof(CanCreateCurrent));
        OnPropertyChanged(nameof(PreviewSummaryText));
        PreviewCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>结束当前会话并清空本次累计状态；调用方必须先读取 Dialog 结果。</summary>
    private void EndSessionCore()
    {
        _isSessionActive = false;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        DetachItemEvents();
        DetachTargetEvents();
        DetachWarningEvents();
        Items.Clear();
        Targets.Clear();
        Warnings.Clear();
        PreviewLines.Clear();
        SkippedItems.Clear();
        Results.Clear();
        _preview = null;
        _pendingCreateCommand = null;
        HasUnknownCreateResult = false;
        HasAppliedResults = false;
        IsCreateInProgress = false;
        _inputRevision = 0;
        _previewSequence = 0;
        ResultSummary = string.Empty;
        StatusText = "尚未生成转采购预览。";
        IsStale = true;
        NotifyPreviewStateChanged();
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
