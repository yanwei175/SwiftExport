using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
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
using System.Text.Json;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>保存采购准备快照并直接创建草稿；失败保留输入，未知结果仅重试原请求。</summary>
public sealed partial class PurchaseDraftWorkflowViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseDraftApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly ICurrentUserService? _currentUser;
    private readonly PurchaseDraftPendingSession _pendingSession;
    private string _ownerUser = string.Empty;
    private string? _inputSnapshotJson;
    private List<PurchaseCandidateItemModel> _recoveryRows = [];
    private bool _pendingRetained;
    private bool _isPreparing;
    private string? _pendingJson;
    private bool _active, _disposed;
    private long _session;
    private CancellationTokenSource? _appendCancellation;
    /// <summary>取得确定逐来源结果后通知容器展示结果。</summary>
    public event EventHandler? CreationCompleted;
    /// <summary>注入创建接口与统一结果边界；准备阶段不调用采购预览接口。</summary>
    public PurchaseDraftWorkflowViewModel(IPurchaseDraftApi api, IProcurementMasterDataQueryApi masterData,
        IApiCallRunner apiCallRunner, IMessageService messageService, PurchasePreparationService? preparationService = null, ICurrentUserService? currentUser = null,
        PurchaseDraftPendingSession? pendingSession = null)
    {
        _api = api; _runner = apiCallRunner; _messages = messageService; _currentUser = currentUser;
        _pendingSession = pendingSession ?? new PurchaseDraftPendingSession();
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        Title = "建立采购草稿"; RegisterBusyAwareCommand(CreateCommand);
    }
    /// <summary>本次已本地校验的完整输入，包含本地筛选隐藏项。</summary>
    public ObservableCollection<PurchaseDraftSelectionItemModel> Items { get; } = [];
    /// <summary>本会话累计逐来源结果。</summary>
    public ObservableCollection<PurchaseDraftCreateResultModel> Results { get; } = [];
    /// <summary>准备页按来源和供应商显式选择新建或追加，不能自动切换目标。</summary>
    public ObservableCollection<PurchaseDraftTargetSelectionModel> Targets { get; } = [];
    /// <summary>采购行生成方式。</summary>
    public IReadOnlyList<NetDemandFilterOption<PurchaseDraftLineModeEnum>> LineModeOptions { get; } = [new(PurchaseDraftLineModeEnum.KeepSourceLines, "不合并"), new(PurchaseDraftLineModeEnum.MergeCompatibleLines, "合并")];
    /// <summary>仅表示本地输入已完成基础校验；服务端仍将在提交时锁内复核。</summary>
    public bool HasValidPreparation => _active && !_isPreparing && !IsCreateInProgress && !HasUnknownCreateResult && !IsStale && Items.Count > 0 && ValidateItems() is null;
    /// <summary>执行中不关闭；返回未知后原请求由应用单例保留，允许关闭弹窗。</summary>
    public bool CanClose => !IsCreateInProgress;
    /// <summary>准备页输入许可。</summary>
    public bool CanEditInputs => _active && !_isPreparing && _pendingJson is null && CanClose && !HasUnknownCreateResult;
    /// <summary>已冻结的原请求即使尚未发送，也不能边改页面边发送旧负载。</summary>
    public bool HasFrozenRequest => _pendingJson is not null;
    /// <summary>当前未决请求的关联身份，恢复后保持不变。</summary>
    public string RequestIdentityText => _ownerUser != CurrentUserKey || ExecutionRequestId == Guid.Empty ? string.Empty : $"执行请求 ID：{(LastSubmittedRequestId == Guid.Empty ? ExecutionRequestId : LastSubmittedRequestId):D}";
    /// <summary>重试的次数和最后时间，不将相同未知回执表现为按钮无反应。</summary>
    public string AttemptText => _ownerUser != CurrentUserKey ? string.Empty : AttemptCount == 0 ? "尚未提交" : $"原请求已尝试 {AttemptCount} 次，最近尝试 {LastAttemptAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}。";
    /// <summary>会话只在应用内保留；关闭弹窗与退出客户端都不表示服务端取消。</summary>
    public string RecoveryHint => _pendingJson is null ? string.Empty
        : "原请求和输入仅在本次客户端运行期间保留。可关闭弹窗后从采购入口重开并重试原提交；退出客户端后不再保留。关闭不会取消服务端操作。";
    /// <summary>恢复的原始所选行，仅用于重建冻结界面。</summary>
    public IReadOnlyList<PurchaseCandidateItemModel> RecoveryRows => _recoveryRows;
    /// <summary>当前不能创建的业务原因。</summary>
    public string CreateBlockReason => HasUnknownCreateResult ? "结果待核实，请重试原提交。" : IsCreateInProgress ? "正在创建采购草稿。"
        : ValidateItems() ?? (IsStale ? "输入已修改，请重新进行本地校验。" : string.Empty);
    [ObservableProperty] public partial PurchaseDraftEntryModeEnum EntryMode { get; private set; }
    [ObservableProperty] public partial Guid ExecutionRequestId { get; private set; }
    [ObservableProperty] public partial PurchaseDraftLineModeEnum LineMode { get; set; } = PurchaseDraftLineModeEnum.MergeCompatibleLines;
    [ObservableProperty] public partial bool IsStale { get; private set; } = true;
    [ObservableProperty] public partial bool HasAppliedResults { get; private set; }
    [ObservableProperty] public partial bool IsCreateInProgress { get; private set; }
    [ObservableProperty] public partial bool HasUnknownCreateResult { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "请选择物料并进行本地校验。";
    [ObservableProperty] public partial string ResultSummary { get; private set; } = string.Empty;
    [ObservableProperty] public partial int AttemptCount { get; private set; }
    [ObservableProperty] public partial DateTimeOffset? LastAttemptAt { get; private set; }
    [ObservableProperty] public partial Guid LastSubmittedRequestId { get; private set; }
    private string CurrentUserKey => _currentUser?.CurrentUser?.UserId ?? "local-test";

    /// <summary>同步恢复当前用户的应用内原会话，不自动查询候选、发送命令或生成新身份。</summary>
    public void OpenSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCreateInProgress) throw new InvalidOperationException("原请求正在执行。");
        var snapshot = _pendingSession.Find(CurrentUserKey);
        if (snapshot is null)
        { _pendingJson = null; _inputSnapshotJson = null; _pendingRetained = false; HasUnknownCreateResult = false; BeginSession(); return; }
        var command = JsonSerializer.Deserialize<CreatePurchaseDraftsCommand>(snapshot.CommandJson)!;
        _session++; _ownerUser = CurrentUserKey; _active = true; _isPreparing = false;
        _pendingJson = snapshot.CommandJson; _inputSnapshotJson = JsonSerializer.Serialize(snapshot); _recoveryRows = snapshot.Rows;
        Items.Clear(); foreach (var item in snapshot.Items) Items.Add(item);
        Targets.Clear(); foreach (var target in snapshot.Targets) Targets.Add(target);
        Results.Clear(); foreach (var result in snapshot.Results) Results.Add(result);
        ExecutionRequestId = snapshot.ExecutionRequestId; EntryMode = command.EntryMode; LastSubmittedRequestId = ExecutionRequestId;
        LineMode = command.Sources[0].LineMode; AttemptCount = snapshot.AttemptCount; LastAttemptAt = snapshot.LastAttemptAt;
        ResultSummary = snapshot.ResultSummary; _pendingRetained = true; HasUnknownCreateResult = true; IsStale = true;
        HasAppliedResults = Results.Any(x => x.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied);
        StatusText = "已恢复本次客户端内待核实的原提交。请显式重试原请求；不会自动创建。";
        NotifyState();
    }
    /// <summary>开始新的 Dialog 会话；未决提交不可被新会话覆盖。</summary>
    public void BeginSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanClose || HasUnknownCreateResult || _pendingSession.Find(CurrentUserKey) is not null) throw new InvalidOperationException("原采购提交尚未核实，不能开始新会话。");
        _appendCancellation?.Cancel(); _appendCancellation = null; _isPreparing = false;
        Items.Clear(); Results.Clear(); Targets.Clear(); _pendingJson = null; _inputSnapshotJson = null; _recoveryRows = []; _session++; _active = true;
        _ownerUser = CurrentUserKey; _pendingRetained = false; AttemptCount = 0; LastAttemptAt = null; LastSubmittedRequestId = Guid.Empty;
        ExecutionRequestId = Guid.NewGuid(); HasAppliedResults = false; IsStale = true; ResultSummary = string.Empty; NotifyState();
    }
    /// <summary>捕获全部所选输入，完成基础校验；不预留容量，也不声称服务端已经验证。</summary>
    public async Task UpdateSelectionAsync(PurchaseDraftPreparationRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active || !CanClose || HasUnknownCreateResult) throw new InvalidOperationException("当前提交尚未核实，不能替换采购输入。");
        EntryMode = request.EntryMode;
        _recoveryRows = request.Rows.ToList();
        Items.Clear();
        foreach (var row in request.Rows.Where(row => !IsSourceCompleted(row.SourceType, row.SourceUid)).DistinctBy(row => row.Uid))
        {
            var item = new PurchaseDraftSelectionItemModel
            {
                NetDemandId = row.Uid, ExpectedVersion = row.Version, ExpectedRemainingQty = row.RemainingQty,
                PurchaseQty = row.PurchaseQty, UnitPrice = row.UnitPrice, TaxPrice = row.TaxPrice, PlannedDeliveryDate = row.PlannedDeliveryDate,
                CustomerId = row.CustomerId, CustomerCode = row.CustomerCode, CustomerName = row.CustomerName,
                SourceType = row.SourceType, SourceUid = row.SourceUid, SourceNo = row.SourceNo, Sku = row.Sku, LinkSku = row.LinkSku,
                IsCustomerSupplied = row.IsCustomerSupplied, SuggestedPurchaseSupplierId = row.SupplierId == Guid.Empty ? null : row.SupplierId,
                SuggestedPurchaseSupplierCode = row.PurchaseSupplierCode, SuggestedPurchaseSupplierName = row.PurchaseSupplierName,
                SuggestedDeliverySupplierId = row.DeliveryTargetSupplierId, SuggestedDeliverySupplierCode = row.DeliverySupplierCode,
                SuggestedDeliverySupplierName = row.DeliverySupplierName, IsQuantityReadOnly = false
            };
            item.InitializeSupplierSelections(); Items.Add(item);
        }
        IsStale = ValidateItems(false) is not null;
        StatusText = ValidateItems(false) ?? "本地输入检查完成；提交时由服务端复核产品、关系和可创建数量。";
        if (IsStale) { Targets.Clear(); NotifyState(); return; }
        var oldTargets = Targets.ToArray(); Targets.Clear();
        _appendCancellation?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _appendCancellation = cts; var session = _session;
        _isPreparing = true; NotifyState();
        try
        {
            foreach (var group in Items.GroupBy(x => (x.SourceType, x.SourceUid, x.SupplierId)))
            {
                var options = new List<PurchaseDraftTargetChoiceModel> { new() { TargetMode = PurchaseDraftTargetModeEnum.CreateNew, DisplayText = "新建采购草稿" } };
                var response = CanAppendDraft
                    ? await _runner.RunAsync(() => _api.GetAppendCandidatesAsync(group.Key.SourceType, group.Key.SourceUid, group.Key.SupplierId, cts.Token), cts.Token, "加载追加草稿候选失败")
                    : ServiceResult<List<SafePurchaseDraftCandidateDto>>.Fail(ServiceErrorCodes.Forbidden, "追加草稿需要采购读取、创建及编辑权限");
                if (_disposed || !_active || session != _session || cts.IsCancellationRequested || !ReferenceEquals(_appendCancellation, cts)) return;
                if (CanAppendDraft && response.Success && response.Data is not null)
                    options.AddRange(response.Data.Select(x => new PurchaseDraftTargetChoiceModel { TargetMode = PurchaseDraftTargetModeEnum.AppendExisting, ExistingPurchaseOrderId = x.PurchaseOrderId, ExpectedPurchaseOrderVersion = x.Version, DisplayText = $"追加到 {x.OrderNo}" }));
                else StatusText += $" {response.Message}；仍可明确选择新建草稿。";
                var old = oldTargets.FirstOrDefault(x => x.SourceType == group.Key.SourceType && x.SourceUid == group.Key.SourceUid && x.SupplierId == group.Key.SupplierId)?.SelectedChoice;
                var selected = old is null ? options[0] : options.FirstOrDefault(x => x.TargetMode == old.TargetMode && x.ExistingPurchaseOrderId == old.ExistingPurchaseOrderId && x.ExpectedPurchaseOrderVersion == old.ExpectedPurchaseOrderVersion);
                if (selected is null && old is not null)
                {
                    selected = new PurchaseDraftTargetChoiceModel { TargetMode = old.TargetMode, ExistingPurchaseOrderId = old.ExistingPurchaseOrderId,
                        ExpectedPurchaseOrderVersion = old.ExpectedPurchaseOrderVersion, DisplayText = old.DisplayText + "（当前不可追加）", IsAvailable = false };
                    options.Add(selected); StatusText += " 原追加目标已保留，请重试加载或明确选择新建。";
                }
                var target = new PurchaseDraftTargetSelectionModel { SourceType = group.Key.SourceType, SourceUid = group.Key.SourceUid, SourceNo = group.First().SourceNo,
                    SupplierId = group.Key.SupplierId, SupplierName = group.First().PurchaseSupplierSearchText, Choices = options };
                target.SetInitialChoice(selected ?? options[0]); target.ValueChanged += OnTargetChanged; Targets.Add(target);
            }
        }
        finally { if (ReferenceEquals(_appendCancellation, cts)) { _appendCancellation = null; _isPreparing = false; } cts.Dispose(); NotifyState(); }
    }
    /// <summary>准备页输入变化只失效本地校验，不清除输入或累计错误。</summary>
    public void InvalidatePreparation() { if (!CanEditInputs) return; IsStale = true; NotifyState(); }
    partial void OnLineModeChanged(PurchaseDraftLineModeEnum value) => InvalidatePreparation();
    /// <summary>按来源直接创建；任何未知响应保留整份不可变请求，成功来源不会重复添加本地结果。</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (!CanCreate()) return;
        if (!_pendingSession.TryBeginSend(_ownerUser)) { StatusText = "同一账号已有原请求正在发送，请等待结果后重试。"; return; }
        var session = _session;
        IsCreateInProgress = true; NotifyState();
        try
        {
            if (_pendingJson is null)
            {
                AttemptCount = 0; LastAttemptAt = null;
                _pendingJson = JsonSerializer.Serialize(BuildCommand());
                _inputSnapshotJson = JsonSerializer.Serialize(new PurchaseDraftRecoverySnapshot { ExecutionRequestId = ExecutionRequestId, CommandJson = _pendingJson,
                    Rows = _recoveryRows, Items = Items.ToList(), Targets = Targets.ToList() });
            }
            var frozen = _pendingJson;
            if (!RetainPending())
            {
                _pendingJson = null; _inputSnapshotJson = null; _pendingRetained = false; IsStale = true;
                StatusText = "已有其他待核实采购会话，本次未发送。请关闭本弹窗后重开，以恢复已有的原请求。";
                return;
            }
            AttemptCount++; LastAttemptAt = DateTimeOffset.Now;
            LastSubmittedRequestId = ExecutionRequestId;
            StatusText = HasUnknownCreateResult ? "正在重试原提交，请等待本次核实结果。" : "正在提交采购创建，请等待服务端结果。";
            NotifyState();
            await RunWriteAsync(async () =>
            {
                var command = JsonSerializer.Deserialize<CreatePurchaseDraftsCommand>(frozen)!;
                ServiceResult<CreatePurchaseDraftsResult> result;
                try { result = await _runner.RunAsync(() => _api.CreateAsync(command, CancellationToken.None), "创建采购草稿失败"); }
                catch
                {
                    if (_active && !_disposed && session == _session && CurrentUserKey == _ownerUser)
                    { ResultSummary = "创建连接异常，结果仍待核实。"; MarkUnknown(); }
                    return;
                }
                if (!_active || _disposed || session != _session || CurrentUserKey != _ownerUser) return;
                if (!result.Success || result.Data is null)
                {
                    var code = result.Error?.Code;
                    ResultSummary = $"{string.Join("、", Items.Select(x => x.SourceNo).Distinct())}：{result.Message}"
                        + (string.IsNullOrWhiteSpace(code) ? string.Empty : $"（{code}）");
                    if (string.IsNullOrWhiteSpace(code) || code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.Unexpected or PurchaseDraftErrorCodes.IdempotencyPartialState
                        || (HasUnknownCreateResult && code is ServiceErrorCodes.RateLimited or ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated))
                    { MarkUnknown(); return; }
                    _pendingSession.Clear(_ownerUser, ExecutionRequestId);
                    _pendingJson = null; _inputSnapshotJson = null; _pendingRetained = false; HasUnknownCreateResult = false; IsStale = true;
                    ExecutionRequestId = Guid.NewGuid(); StatusText = "创建未完成，全部输入已保留，请修正或刷新后重新校验。";
                    _messages.ShowWarning("采购草稿未创建", ResultSummary); return;
                }
                var groups = command.Sources.Select(x => (x.SourceType, x.SourceUid)).ToHashSet();
                if (result.Data.ExecutionRequestId != command.ExecutionRequestId || result.Data.Sources is null
                    || result.Data.Sources.Count != groups.Count
                    || result.Data.Sources.Any(x => x is null)
                    || result.Data.Sources.Any(x => x.PurchaseDrafts?.Any(d => d is null) == true)
                    || result.Data.Sources.Select(x => (x.SourceType, x.SourceUid)).Distinct().Count() != groups.Count
                    || result.Data.Sources.Any(x => !Enum.IsDefined(x.Outcome) || !groups.Contains((x.SourceType, x.SourceUid)))
                    || result.Data.Sources.Any(x => x.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied
                        && (x.PurchaseDrafts is null || x.PurchaseDrafts.Count == 0 || x.PurchaseDrafts.Any(d => d is null || d.PurchaseOrderId == Guid.Empty || d.Version <= 0))))
                { ResultSummary = "返回来源或请求身份不完整，创建结果仍待核实。"; MarkUnknown(); return; }
                var unresolved = result.Data.Sources.Any(x => x.ErrorCode is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.Unexpected or PurchaseDraftErrorCodes.IdempotencyPartialState
                    || (x.Outcome is not NetDemandCommandOutcomeEnum.Succeeded and not NetDemandCommandOutcomeEnum.AlreadyApplied && string.IsNullOrWhiteSpace(x.ErrorCode)));
                foreach (var source in result.Data.Sources)
                {
                    var old = Results.FirstOrDefault(x => x.SourceType == source.SourceType && x.SourceUid == source.SourceUid);
                    if (old is not null) Results.Remove(old);
                    Results.Add(PurchaseDraftCreateResultModel.FromDto(source, Items.FirstOrDefault(x => x.SourceType == source.SourceType && x.SourceUid == source.SourceUid)?.SourceNo ?? old?.SourceNo ?? string.Empty));
                }
                HasAppliedResults |= result.Data.Sources.Any(x => x.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied);
                var completed = Results.Where(x => x.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied).ToArray();
                var unknownCount = Results.Count(x => IsUnknownSource(x));
                ResultSummary = $"成功或已应用 {completed.Length}，待核实 {unknownCount}，确定失败 {Results.Count - completed.Length - unknownCount}。" + Environment.NewLine
                    + string.Join(Environment.NewLine, Results.Select(x => $"{x.SourceDisplay}：{x.Message}" + (string.IsNullOrWhiteSpace(x.ErrorCode) ? string.Empty : $"（{x.ErrorCode}）")));
                if (unresolved) { MarkUnknown(); return; }
                _pendingSession.Clear(_ownerUser, ExecutionRequestId);
                foreach (var item in Items.Where(x => IsSourceCompleted(x.SourceType, x.SourceUid)).ToArray()) Items.Remove(item);
                _pendingJson = null; _inputSnapshotJson = null; _pendingRetained = false; HasUnknownCreateResult = false; IsStale = true; ExecutionRequestId = Guid.NewGuid();
                StatusText = "已接纳确定结果；失败来源输入保留，可返回准备修正。";
                CreationCompleted?.Invoke(this, EventArgs.Empty);
            }, "正在创建采购草稿...");
        }
        catch
        {
            if (_pendingRetained) MarkUnknown();
            else { _pendingJson = null; _inputSnapshotJson = null; IsStale = true; StatusText = "本地准备未完成，本次未发送。输入已保留，请重新校验。"; }
        }
        finally
        {
            try { if (_pendingJson is not null && _pendingRetained) RetainPending(); }
            finally { IsCreateInProgress = false; _pendingSession.EndSend(_ownerUser); NotifyState(); }
        }
    }
    private static bool IsUnknownSource(PurchaseDraftCreateResultModel source) => source.ErrorCode is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy
        or ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.Unexpected or PurchaseDraftErrorCodes.IdempotencyPartialState
        || (source.Outcome is not NetDemandCommandOutcomeEnum.Succeeded and not NetDemandCommandOutcomeEnum.AlreadyApplied && string.IsNullOrWhiteSpace(source.ErrorCode));
    /// <summary>应用内更新反馈时始终使用首次冻结输入，页面值变化不污染原提交。</summary>
    private bool RetainPending()
    {
        var original = JsonSerializer.Deserialize<PurchaseDraftRecoverySnapshot>(_inputSnapshotJson!)!;
        var retained = _pendingSession.TrySave(_ownerUser, new PurchaseDraftRecoverySnapshot
        {
            ExecutionRequestId = original.ExecutionRequestId, CommandJson = original.CommandJson, Rows = original.Rows, Items = original.Items, Targets = original.Targets,
            Results = Results.ToList(), AttemptCount = AttemptCount, LastAttemptAt = LastAttemptAt, ResultSummary = ResultSummary
        });
        if (retained) _pendingRetained = true;
        return retained;
    }
    /// <summary>页面包装器会捕获异常，因此在此保留未决身份，避免异常被吞后误开放编辑。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { ResultSummary = "处理创建回执时发生异常，原请求仍待核实。"; MarkUnknown(); return Task.CompletedTask; }
    /// <summary>仅捕获用户输入与需求版本，不向创建接口传递预览token或产品快照。</summary>
    private CreatePurchaseDraftsCommand BuildCommand() => new()
    {
        ExecutionRequestId = ExecutionRequestId, EntryMode = EntryMode,
        Sources = Items.GroupBy(x => (x.SourceType, x.SourceUid)).Select(group => new CreatePurchaseDraftSourceCommand
        {
            SourceType = group.Key.SourceType, SourceUid = group.Key.SourceUid, LineMode = LineMode,
            Targets = Targets.Where(x => x.SourceType == group.Key.SourceType && x.SourceUid == group.Key.SourceUid).Select(x => x.ToCreateDto()).ToList(),
            Items = group.Select(item => new CreatePurchaseDraftItemCommand
            {
                CommandItemId = item.CommandItemId, NetDemandId = item.NetDemandId, ExpectedVersion = item.ExpectedVersion,
                PurchaseQty = item.PurchaseQty, SupplierId = item.SupplierId, DeliveryTargetSupplierId = item.DeliveryTargetSupplierId,
                UnitPrice = item.UnitPrice, TaxPrice = item.TaxPrice, PlannedDeliveryDate = item.PlannedDeliveryDate
            }).ToList()
        }).ToList()
    };
    /// <summary>确定成功的来源不能被再次加入新提交。</summary>
    public bool IsSourceCompleted(string type, Guid uid) => Results.Any(x => x.SourceType == type && x.SourceUid == uid && x.Outcome is NetDemandCommandOutcomeEnum.Succeeded or NetDemandCommandOutcomeEnum.AlreadyApplied);
    /// <summary>刷新候选时保留已成功来源标记，防止将同一来源重新加入新提交。</summary>
    public IReadOnlyList<string> GetCompletedSourceKeys() => Results.Where(x => IsSourceCompleted(x.SourceType, x.SourceUid))
        .Select(x => $"{x.SourceType.Trim().ToUpperInvariant()}|{x.SourceUid:N}").Distinct().ToArray();
    /// <summary>追加目标需要可读取、可创建且可编辑；新建不额外要求读取或编辑权限。</summary>
    private bool CanAppendDraft => _currentUser?.HasPermission(PermissionKeys.ProcurementOrderRead) == true
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate) && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit);
    private string? ValidateItems(bool includeTargets = true) => Items.Count == 0 ? "请至少选择一条物料。" : Items.Any(x => x.SupplierId == Guid.Empty) ? "请明确选择实际采购供应商。"
        : Items.Any(x => x.IsCustomerSupplied && !x.DeliveryTargetSupplierId.HasValue) ? "请明确选择客供件送达供应商。"
        : includeTargets && Targets.Any(x => !x.IsCreateNew) && !CanAppendDraft ? "追加草稿需要采购读取、创建及编辑权限；仍可明确选择新建草稿。"
        : includeTargets && Targets.Any(x => x.SelectedChoice?.IsAvailable == false) ? "原追加目标当前不可用，请重新加载或明确选择新建草稿。"
        : Items.Select(x => x.InputError).FirstOrDefault(x => x is not null);
    private void OnTargetChanged(object? sender, EventArgs e) => NotifyState();
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_ownerUser != CurrentUserKey)
        {
            _session++; _active = false;
            Items.Clear(); Targets.Clear(); Results.Clear(); _recoveryRows = []; ResultSummary = string.Empty;
            StatusText = "当前账号已切换。原请求在本次客户端内按原账号保留，请关闭后重新进入采购入口。";
        }
        NotifyState();
    }
    private bool CanCreate() => _active && !_disposed && !IsCreateInProgress && IsNotBusy && CanWrite && _ownerUser == CurrentUserKey
        && (_currentUser?.HasPermission(PermissionKeys.ProcurementOrderCreate) ?? true)
        && (!Targets.Any(x => !x.IsCreateNew) || CanAppendDraft)
        && (_pendingJson is not null ? _pendingRetained : HasValidPreparation);
    private void MarkUnknown() { HasUnknownCreateResult = true; IsStale = true; StatusText = "创建结果待核实。请查看本次反馈并重试原提交，或保留关闭后再恢复；不要新建替代请求。"; NotifyState(); }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanClose)); OnPropertyChanged(nameof(CanEditInputs)); OnPropertyChanged(nameof(HasValidPreparation)); OnPropertyChanged(nameof(CreateBlockReason));
        OnPropertyChanged(nameof(RequestIdentityText)); OnPropertyChanged(nameof(AttemptText)); OnPropertyChanged(nameof(RecoveryHint));
        OnPropertyChanged(nameof(HasFrozenRequest));
        CreateCommand.NotifyCanExecuteChanged();
    }
    /// <summary>只有可关闭时结束会话，未知请求不得被容器静默清除。</summary>
    public void Close() { if (!CanClose) return; _session++; _active = false; _appendCancellation?.Cancel(); if (!HasUnknownCreateResult) { Items.Clear(); Targets.Clear(); _pendingJson = null; } NotifyState(); }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) return; if (!CanClose) return; Close(); if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged; _disposed = true; }
}
