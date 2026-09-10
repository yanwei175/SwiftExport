using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

public sealed partial class PurchaseDraftPreviewViewModel : ViewModelBaseEx, IDisposable
{
    private readonly INetDemandWorkspaceApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _createCancellation;
    private PurchaseDraftPreviewResponse? _preview;
    private bool _suppressStale;
    private bool _disposed;

    public PurchaseDraftPreviewViewModel(
        INetDemandWorkspaceApi api,
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
    public bool CanClose => !IsCreateInProgress;

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
    public partial bool IsCreateInProgress { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "尚未生成转采购预览。";

    [ObservableProperty]
    public partial string ResultSummary { get; private set; } = string.Empty;

    public async Task OpenAsync(PurchaseDraftPreviewOpenRequest request, CancellationToken cancellationToken = default)
    {
        Reset();
        _suppressStale = true;
        EntryMode = request.EntryMode;
        ExecutionRequestId = Guid.NewGuid();
        SourceType = request.SourceType;
        SourceUid = request.SourceUid;
        SourceNo = request.SourceNo;
        LineMode = PurchaseDraftLineModeEnum.KeepSourceLines;
        foreach (var row in request.Rows)
        {
            AddItem(new PurchaseDraftSelectionItemModel
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
                IsQuantityReadOnly = request.EntryMode != PurchaseDraftEntryModeEnum.ManualSelection,
            });
        }
        _suppressStale = false;
        IsStale = true;
        await LoadPreviewAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPreview))]
    private Task PreviewAsync() => LoadPreviewAsync();

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
        StatusText = "正在重新计算采购草稿预览...";

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.PreviewPurchaseDraftsAsync(CreatePreviewRequest(), cancellation.Token),
                    cancellation.Token,
                    "生成采购草稿预览失败");
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_previewCancellation, cancellation))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    StatusText = result.Message;
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
        var preview = _preview;
        if (preview is null)
        {
            return;
        }

        _createCancellation?.Cancel();
        _createCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _createCancellation = cancellation;
        IsCreateInProgress = true;
        try
        {
            await RunWriteAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.CreatePurchaseDraftsAsync(CreateCommandDto(preview), cancellation.Token),
                    cancellation.Token,
                    "创建采购草稿失败");
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_createCancellation, cancellation))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    _messageService.ShowError("创建采购草稿失败", result.Message);
                    return;
                }

                Results.Clear();
                foreach (var source in result.Data.Sources)
                {
                    Results.Add(PurchaseDraftCreateResultModel.FromDto(source));
                }

                var succeeded = Results.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.Succeeded);
                var already = Results.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.AlreadyApplied);
                var failed = Results.Count - succeeded - already;
                HasAppliedResults = succeeded + already > 0;
                ResultSummary = $"成功 {succeeded}，已应用 {already}，失败 {failed}。";
                IsStale = true;
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
            if (ReferenceEquals(_createCancellation, cancellation))
            {
                _createCancellation = null;
            }
            IsCreateInProgress = false;
            cancellation.Dispose();
            PreviewCommand.NotifyCanExecuteChanged();
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
        Targets = Targets.Select(item => item.ToPreviewDto()).ToList(),
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
            Results.Clear();
            ResultSummary = string.Empty;
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
                    PreviewLines.Add(new PurchaseDraftPreviewLineModel
                    {
                        SourceNo = source.SourceNo,
                        SupplierName = $"{document.SupplierCode} {document.SupplierName}".Trim(),
                        Sku = line.Sku ?? string.Empty,
                        CustomerSku = line.CustomerSku ?? string.Empty,
                        PurchaseQty = line.PurchaseQty,
                        PlannedDeliveryDate = line.PlannedDeliveryDate,
                        DeliveryTargetSupplierName = line.DeliveryTargetSupplierName ?? string.Empty,
                        PurchaseDataIncomplete = line.PurchaseDataIncomplete,
                        ContributionCount = line.Contributions.Count,
                    });
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
        if (_suppressStale || _preview is null)
        {
            return;
        }

        IsStale = true;
        StatusText = "预览参数已修改，必须重新预览后才能创建采购草稿。";
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

    private bool CanPreview() => _createCancellation is null && ValidateItems() is null;

    private bool CanCreate() => CanWrite
        && IsNotBusy
        && _preview is not null
        && !IsStale
        && !HasBlockers
        && _preview.SourceGroups.All(group => group.CanCreate)
        && !HasUnconfirmedWarnings;

    private void NotifyPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(HasUnconfirmedWarnings));
        OnPropertyChanged(nameof(CanCreateCurrent));
        PreviewCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
    }

    private void Reset()
    {
        _previewCancellation?.Cancel();
        _createCancellation?.Cancel();
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
        HasAppliedResults = false;
        IsCreateInProgress = false;
        ResultSummary = string.Empty;
        StatusText = "尚未生成转采购预览。";
        IsStale = true;
        NotifyPreviewStateChanged();
    }

    public void Close()
    {
        Reset();
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
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _createCancellation?.Cancel();
        _createCancellation?.Dispose();
        DetachItemEvents();
        DetachTargetEvents();
        DetachWarningEvents();
    }
}
