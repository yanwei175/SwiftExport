using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;

/// <summary>售后详情文档；维护投诉事实并执行实物确认、撤回和生命周期命令。</summary>
public sealed partial class AfterSalesDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "quality-control-after-sales-detail";
    private readonly IAfterSalesApi _api;
    private readonly IAfterSalesFeatureService _feature;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IAfterSalesReasonDialogService _reasonDialog;
    private readonly IMessageService _messages;
    private readonly ICurrentUserService _currentUser;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly IProductApi _productApi;
    private readonly IAfterSalesQualityCategoryApi _qualityCategoryApi;
    private CancellationTokenSource? _categoryCancellation;
    private int _productSearchVersion;
    private int _detailVersion;
    private PendingWrite? _pendingWrite;
    private Guid? _refreshOnlyUid;
    private PendingDemandConfirmation? _pendingConfirmation;
    private CancellationTokenSource? _loadCancellation;
    private bool _hasServerIdentity;
    private bool _loading;
    private bool _disposed;

    /// <summary>创建售后详情文档并接入业务命令、对话框、导航、权限及基础资料缓存。</summary>
    public AfterSalesDetailViewModel(IAfterSalesApi api, IAfterSalesFeatureService feature, IApiCallRunner runner,
        INavigationService navigation, IDialogService dialogs, IAfterSalesReasonDialogService reasonDialog,
        IMessageService messages, ICurrentUserService currentUser, IReferenceDataCacheService referenceDataCache, IProductApi productApi,
        IAfterSalesQualityCategoryApi qualityCategoryApi)
    {
        _api = api; _feature = feature; _runner = runner; _navigation = navigation; _dialogs = dialogs;
        _reasonDialog = reasonDialog; _messages = messages; _currentUser = currentUser;
        _referenceDataCache = referenceDataCache;
        _productApi = productApi;
        _qualityCategoryApi = qualityCategoryApi;
        Title = "售后服务单-新建";
        PropertyChanged += OnBusyPropertyChanged;
        Header.PropertyChanged += OnEditChanged;
        LineList.ListStateChanged += OnLineListChanged;
        RegisterBusyAwareCommand(SaveCommand); RegisterBusyAwareCommand(ConfirmDemandCommand);
        RegisterBusyAwareCommand(CancelDemandConfirmationCommand); RegisterBusyAwareCommand(CloseCommand);
        RegisterBusyAwareCommand(ReopenCommand); RegisterBusyAwareCommand(VoidCommand);
        RegisterBusyAwareCommand(AddLineCommand); RegisterBusyAwareCommand(DeleteLineCommand);
        RegisterBusyAwareCommand(ReloadCommand); RegisterBusyAwareCommand(RefreshLookupsCommand);
        RegisterBusyAwareCommand(RefreshQualityCategoriesCommand);
    }

    public AfterSalesHeaderEditModel Header { get; } = new();
    /// <summary>补件变更集合，由公共列表模型维护新增、修改和显式删除。</summary>
    public BaseListModel<AfterSalesLineModel, AfterSalesLineDto> LineList { get; } = new(_ => new());
    /// <summary>当前可见补件。</summary>
    public ReadOnlyObservableCollection<AfterSalesLineModel> Lines => LineList.Items;
    /// <summary>提交前由视图结束原生表格编辑；失败时不得发送旧值。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>获取供客户选择控件使用的完整客户缓存。</summary>
    public ReadOnlyObservableCollection<CustomerDto> Customers => _referenceDataCache.Customers;
    public ObservableCollection<AfterSalesLookupDto> Owners { get; } = [];
    /// <summary>获取供责任方选择控件使用的完整供应商缓存。</summary>
    public ReadOnlyObservableCollection<SupplierDto> Suppliers => _referenceDataCache.Suppliers;
    /// <summary>问题产品及补件共用的当前远程搜索结果。</summary>
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    [ObservableProperty] public partial ProductLookupDto? SelectedProductCandidate { get; set; }
    [ObservableProperty] public partial bool IsProductSearchBusy { get; set; }
    public ObservableCollection<AfterSalesLookupDto> SalesOrderLines { get; } = [];
    /// <summary>获取供补件颜色选择控件使用的完整颜色缓存。</summary>
    public ReadOnlyObservableCollection<ProductColorDto> Colors => _referenceDataCache.ProductColors;
    /// <summary>完整分类树含停用节点，供历史显示；选择资格由树选择器及后端分别验证。</summary>
    [ObservableProperty] public partial IReadOnlyList<AfterSalesQualityCategoryNode> QualityCategories { get; set; } = [];
    /// <summary>分类候选独立加载状态，不覆盖单据写入 Busy。</summary>
    [ObservableProperty] public partial bool IsQualityCategoryLoading { get; set; }
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.AfterSalesNo) ? "售后服务单-新建" : Header.AfterSalesNo;
    public bool HasUnsavedChanges => IsDirty || HasPendingIntent;
    /// <summary>表示页面仍持有原写入意图，直到取得确定结果并读到当前详情。</summary>
    public bool HasPendingIntent => _pendingWrite is not null || _pendingConfirmation is not null || _refreshOnlyUid.HasValue;
    public bool IsPersisted => _hasServerIdentity;
    public bool HasAccess => _currentUser.HasPermission("QualityControl.AfterSales.View");
    public string DocumentStatusText => DocumentStatus switch { DocumentStatusEnum.Closed => "已结案", DocumentStatusEnum.Voided => "已作废", _ => "开放" };
    public string ConfirmationStatusText => ConfirmationStatus == ConfirmationStatusEnum.已确认 ? "实物需求已确认" : "实物需求待确认";
    public bool CanMaintain => _currentUser.HasPermission(PermissionKeys.AfterSalesMaintain);
    public bool CanConfirmDemand => _currentUser.HasPermission(PermissionKeys.AfterSalesConfirm);
    public bool CanCancelDemand => _currentUser.HasPermission(PermissionKeys.AfterSalesCancelConfirmation);
    public bool CanManageLifecycle => _currentUser.HasPermission(PermissionKeys.AfterSalesLifecycle);
    public bool CanEditSupplement => Capabilities.CanSave && CanMaintain && !HasPendingIntent && !IsBusy;
    public bool CanEditFacts => Capabilities.CanEditFacts && CanMaintain && !HasPendingIntent && !IsBusy;

    /// <summary>加载和提交结束时重新通知实际输入控件，避免 Busy 内清除未决后仍保持禁用。</summary>
    private void OnBusyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IsBusy)) return;
        OnPropertyChanged(nameof(CanEditFacts));
        OnPropertyChanged(nameof(CanEditSupplement));
    }

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteLineCommand))] public partial AfterSalesLineModel? SelectedLine { get; set; }
    /// <summary>获取或设置售后单头当前选中的完整客户。</summary>
    [ObservableProperty] public partial CustomerDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial AfterSalesCapabilitiesDto Capabilities { get; set; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DocumentStatusText))] public partial DocumentStatusEnum DocumentStatus { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ConfirmationStatusText))] public partial ConfirmationStatusEnum ConfirmationStatus { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "新建售后服务单。";
    /// <summary>视图数字或日期转换错误会阻止提交，避免保存模型中残留的旧值。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDemandCommand))]
    public partial bool HasInputErrors { get; set; }

    partial void OnCapabilitiesChanged(AfterSalesCapabilitiesDto value)
    {
        OnPropertyChanged(nameof(CanEditFacts)); OnPropertyChanged(nameof(CanEditSupplement)); NotifyCommands();
    }

    partial void OnSelectedCustomerChanged(CustomerDto? value)
    {
        if (_loading) return;
        Header.CustomerId = value?.Uid ?? Guid.Empty;
        Header.CustomerCodeSnapshot = value?.CustomerCode ?? string.Empty;
        Header.CustomerNameSnapshot = value?.CustomerName ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (!HasAccess) { IsReadOnly = true; ResultSummary = "当前账号没有品质管理权限。"; return; }
        var request = parameter as AfterSalesDocumentOpenRequest;
        if (request?.AfterSalesId is not Guid uid || uid == Guid.Empty)
        {
            InitializeNew();
            await LoadLookupsAsync(cancellationToken);
            return;
        }
        await LoadAsync(uid, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (HasPendingIntent)
        {
            _messages.ShowWarning("售后确认结果未定", "请使用原请求重试确认，取得确定结果后再关闭页签。");
            return false;
        }
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭售后服务单", "当前服务单有未保存修改，确定放弃并关闭吗？", "放弃并关闭", "继续编辑", cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private void AddLine()
    {
        var line = LineList.AddNew(new()
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            Version = 1,
            LineNumber = LineList.AllLines.Count == 0 ? 1 : LineList.AllLines.Max(x => x.LineNumber) + 1
        });
        SelectedLine = line; UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanDeleteLine))]
    private void DeleteLine()
    {
        var line = SelectedLine!;
        LineList.Remove(line); SelectedLine = Lines.FirstOrDefault(); UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_pendingWrite is null)
        {
            if (!Validate(false)) return;
            var command = CreateSaveCommand();
            var create = !IsPersisted;
            SetPendingWrite(new PendingWrite("save", command.Uid, "售后服务单已保存",
                () => _runner.RunAsync(() => create ? _api.CreateAsync(command) : _api.SaveAsync(command.Uid, command), "保存售后服务单失败")));
        }
        await RunPendingWriteAsync();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmDemandAsync()
    {
        if (_pendingConfirmation is null)
        {
            if (!Validate(true)) return;
            if (!Lines.Any(x => x.Quantity > 0)) { _messages.ShowInfo("无需确认", "当前没有正数量补发要求，投诉单可以直接保存并在处理完成后结案。"); return; }
            if (!await _dialogs.ConfirmAsync("确认实物需求", "将先保存当前修改，再把正数量补件交给成品需求。确认后相关事实会锁定。", "保存并确认")) return;
            SetPendingConfirmation(new PendingDemandConfirmation(CreateSaveCommand(), !IsPersisted, Guid.NewGuid()));
        }
        await RunBusyAsync(async () =>
        {
            var pending = _pendingConfirmation!;
            if (pending.Confirm is null)
            {
                // 保存结果未知时保留完整原请求、创建标记和 CommandId，不发确认。
                var saved = await _feature.SaveAsync(pending.Save, pending.Create);
                if (!saved.Success || saved.Data is null)
                {
                    if (!MustKeepPendingIntent(saved.Error?.Code, ref pending.SaveOutcomeUnknown))
                    {
                        SetPendingConfirmation(null);
                    }
                    HandleFailure("确认前保存失败", saved);
                    return;
                }
                ApplyCommandResult(saved.Data);
                _navigation.UpdateDocumentKey(this, $"after-sales:{saved.Data.Uid:N}");
                pending.Confirm = new ConfirmAfterSalesDemandCommand
                { CommandId = pending.ConfirmId, Version = saved.Data.Version };
            }
            // 确认结果未知时只重试确认，不能再次保存并推进头版本。
            var confirmed = await _feature.ConfirmAsync(pending.Save.Uid, pending.Confirm);
            if (!confirmed.Success || confirmed.Data is null)
            {
                if (!MustKeepPendingIntent(confirmed.Error?.Code, ref pending.ConfirmOutcomeUnknown))
                {
                    SetPendingConfirmation(null);
                    _refreshOnlyUid = pending.Save.Uid;
                    NotifyPendingState();
                    if (await ReloadCoreAsync(pending.Save.Uid, CancellationToken.None))
                    {
                        _refreshOnlyUid = null;
                        NotifyPendingState();
                    }
                }
                HandleFailure("服务单已保存，但实物需求未确认", confirmed);
                return;
            }
            SetPendingConfirmation(null);
            await RefreshAfterSuccessAsync(pending.Save.Uid, "实物需求已确认");
        }, "正在保存并确认实物需求...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelDemandConfirmationAsync()
    {
        if (_pendingWrite?.Operation == "cancel") { await RunPendingWriteAsync(); return; }
        var reason = await _reasonDialog.RequestAsync("取消实物需求确认", "成品需求会先检查是否能够安全撤回；失败时服务单仍保持确认和锁定状态。");
        if (reason is null) return;
        var uid = Header.Uid;
        var request = new CancelAfterSalesDemandConfirmationCommand { CommandId = Guid.NewGuid(), Version = Header.Version, Reason = reason };
        SetPendingWrite(new PendingWrite("cancel", uid, "已取消实物需求确认",
            () => _runner.RunAsync(() => _api.CancelDemandConfirmationAsync(uid, request), "取消实物需求确认失败")));
        await RunPendingWriteAsync();
    }
    [RelayCommand(CanExecute = nameof(CanCloseDocument))]
    private async Task CloseAsync()
    {
        if (_pendingWrite?.Operation == "close") { await RunPendingWriteAsync(); return; }
        var reason = await _reasonDialog.RequestAsync("结案", "正数量要求必须已正式交付完成或由下游合法关闭。请输入结案说明。");
        if (reason is null) return;
        var uid = Header.Uid;
        var request = new CloseAfterSalesCommand { CommandId = Guid.NewGuid(), Version = Header.Version, Reason = reason };
        SetPendingWrite(new PendingWrite("close", uid, "售后服务单已结案",
            () => _runner.RunAsync(() => _api.CloseAsync(uid, request), "结案失败")));
        await RunPendingWriteAsync();
    }
    [RelayCommand(CanExecute = nameof(CanReopen))]
    private async Task ReopenAsync()
    {
        if (_pendingWrite?.Operation == "reopen") { await RunPendingWriteAsync(); return; }
        var reason = await _reasonDialog.RequestAsync("重开售后服务单", "重开只恢复售后生命周期，不会重新发布成品需求。请输入原因。");
        if (reason is null) return;
        var uid = Header.Uid;
        var request = new ReopenAfterSalesCommand { CommandId = Guid.NewGuid(), Version = Header.Version, Reason = reason };
        SetPendingWrite(new PendingWrite("reopen", uid, "售后服务单已重开",
            () => _runner.RunAsync(() => _api.ReopenAsync(uid, request), "重开失败")));
        await RunPendingWriteAsync();
    }
    [RelayCommand(CanExecute = nameof(CanVoid))]
    private async Task VoidAsync()
    {
        if (_pendingWrite?.Operation == "void") { await RunPendingWriteAsync(); return; }
        var reason = await _reasonDialog.RequestAsync("作废售后服务单", "作废要求服务单开放、未确认且没有活动成品需求。请输入原因。");
        if (reason is null) return;
        var uid = Header.Uid;
        var request = new VoidAfterSalesCommand { CommandId = Guid.NewGuid(), Version = Header.Version, Reason = reason };
        SetPendingWrite(new PendingWrite("void", uid, "售后服务单已作废",
            () => _runner.RunAsync(() => _api.VoidAsync(uid, request), "作废失败")));
        await RunPendingWriteAsync();
    }
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (HasPendingIntent) return;
        if (IsDirty && !await _dialogs.ConfirmAsync("重新加载售后服务单", "重新加载会放弃当前未保存修改，确定继续吗？", "放弃并重新加载", "继续编辑")) return;
        await LoadAsync(Header.Uid, CancellationToken.None);
    }
    [RelayCommand(CanExecute = nameof(CanRefreshLookups))] private Task RefreshLookupsAsync() => LoadLookupsAsync(CancellationToken.None);

    /// <summary>重试页面保存的原请求；确认结果后只查询当前详情。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryPendingWrite))]
    private Task RetryPendingWriteAsync() => RunPendingWriteAsync();

    private bool CanRetryPendingWrite() => _pendingWrite is not null && IsNotBusy;

    /// <summary>刷新失败只重新查询，不重复提交已经成功的命令。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshCurrentFact))]
    private async Task RefreshCurrentFactAsync()
    {
        if (_refreshOnlyUid is Guid uid)
        {
            await RunBusyAsync(async () => await RefreshAfterSuccessAsync(uid, "已刷新售后服务单"), "正在刷新售后服务单...");
        }
    }

    private bool CanRefreshCurrentFact() => _refreshOnlyUid.HasValue && IsNotBusy;

    /// <summary>完整请求由页面持有；前置拒绝只有发生在结果未知之后才保留原意图。</summary>
    private async Task RunPendingWriteAsync()
    {
        if (_pendingWrite is not { } pending) return;
        await RunBusyAsync(async () =>
        {
            var result = await pending.Call();
            if (!result.Success || result.Data is null)
            {
                if (!MustKeepPendingIntent(result.Error?.Code, ref pending.OutcomeUnknown)) SetPendingWrite(null);
                HandleFailure(pending.Success + "失败", result);
                return;
            }
            SetPendingWrite(null);
            _hasServerIdentity = true;
            await RefreshAfterSuccessAsync(result.Data.Uid, pending.Success);
        }, "正在执行售后命令...");
    }

    private void SetPendingWrite(PendingWrite? pending)
    {
        _pendingWrite = pending;
        NotifyPendingState();
    }

    private sealed class PendingWrite(string operation, Guid uid, string success,
        Func<Task<ServiceResult<AfterSalesCommandResult>>> call)
    {
        public string Operation { get; } = operation;
        public Guid Uid { get; } = uid;
        public string Success { get; } = success;
        public Func<Task<ServiceResult<AfterSalesCommandResult>>> Call { get; } = call;
        public bool OutcomeUnknown;
    }

    // 本地验证只负责明显输入错误，状态及跨领域资格仍由后端决定。
    private bool Validate(bool forConfirmation)
    {
        if (CommitPendingEdits?.Invoke() == false) return ValidationFailed("请先完成或修正补件表格中的输入。");
        if (HasInputErrors) return ValidationFailed("请先修正页面中无效的数字或日期。");
        if (Header.CustomerId == Guid.Empty) return ValidationFailed("Header.CustomerId：请选择客户。");
        if (Header.AcceptedDate == default) return ValidationFailed("Header.AcceptedDate：请选择受理日期。");
        var headerError = Header.Validate(forConfirmation && Lines.Any(x => x.Quantity > 0));
        if (headerError is not null) return ValidationFailed(headerError);
        foreach (var line in Lines)
        {
            if (line.IsNew && line.IsEmptyPlaceholder) continue;
            var error = line.Validate(forConfirmation);
            if (error is null) continue;
            SelectedLine = line;
            return ValidationFailed($"第 {line.LineNumber} 行：{error}");
        }
        ValidationSummary = null; return true;
    }

    private bool ValidationFailed(string message) { ValidationSummary = message; _messages.ShowWarning("售后服务单未提交", message); return false; }

    private SaveAfterSalesCommand CreateSaveCommand()
    {
        var command = new SaveAfterSalesCommand
        {
            CommandId = Guid.NewGuid(),
            Uid = Header.Uid,
            Version = Header.Version,
            Header = Header.ToDto(),
            AddedLines = Lines.Where(x => x.IsNew && !x.IsEmptyPlaceholder).Select(x => x.ToEditDto()).ToList(),
            UpdatedLines = Lines.Where(x => !x.IsNew && x.IsDirty).Select(x => x.ToEditDto()).ToList(),
            DeletedLines = LineList.DeletedItems.Select(x => new AfterSalesDeletedLineDto { Uid = x.Uid, Version = x.Version }).ToList()
        };
        return command;
    }

    /// <summary>限流、授权和服务暂不可用均未证明旧意图结局；确定业务失败才结束当前阶段。</summary>
    private static bool MustKeepPendingIntent(string? code, ref bool outcomeUnknown) =>
        (outcomeUnknown |= code is ServiceErrorCodes.OutcomeUnknown
            or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable) && MustKeepPendingIntent(code);

    /// <summary>保留其他已有命令的错误分类；确认流程额外判断该阶段是否曾结果未知。</summary>
    private static bool MustKeepPendingIntent(string? code) => code is
        ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.RateLimited
        or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden or ServiceErrorCodes.ServiceUnavailable;

    /// <summary>统一通知编辑、重试和页签关闭门禁，避免阶段切换遗漏 CanExecute。</summary>
    private void SetPendingConfirmation(PendingDemandConfirmation? pending)
    {
        _pendingConfirmation = pending;
        NotifyPendingState();
    }

    private void NotifyPendingState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasPendingIntent));
        OnPropertyChanged(nameof(CanEditFacts));
        OnPropertyChanged(nameof(CanEditSupplement));
        RetryPendingWriteCommand.NotifyCanExecuteChanged();
        RefreshCurrentFactCommand.NotifyCanExecuteChanged();
        NotifyCommands();
    }

    /// <summary>页面实例内保存两阶段不可替换的原保存请求与确认身份。</summary>
    private sealed class PendingDemandConfirmation(SaveAfterSalesCommand save, bool create, Guid confirmId)
    {
        public SaveAfterSalesCommand Save { get; } = save;
        public bool Create { get; } = create;
        public Guid ConfirmId { get; } = confirmId;
        public ConfirmAfterSalesDemandCommand? Confirm { get; set; }
        // 两阶段独立记录结果未知，保存重放成功不代表确认曾经执行。
        public bool SaveOutcomeUnknown;
        public bool ConfirmOutcomeUnknown;
    }
    private void HandleFailure<T>(string title, ServiceResult<T>? result)
    {
        var message = result?.Error?.Message ?? result?.Message ?? "请求没有返回结果。";
        ResultSummary = title + "：" + message;
        if (!string.IsNullOrWhiteSpace(result?.Error?.Field))
        {
            ValidationSummary = $"{result.Error.Field}：{message}";
            SelectErrorLine(result.Error.Field);
        }
        if (result?.Error?.Code == ServiceErrorCodes.VersionConflict) _messages.ShowWarning("数据已变化", message + " 本地输入已保留，请决定是否刷新。");
        else _messages.ShowError(title, message);
    }

    private void InitializeNew()
    {
        _loading = true; _hasServerIdentity = false; Header.Uid = GuidSequentialGenerator.NewGuid(); Header.Version = 1; Header.AcceptedDate = DateTime.Today;
        DocumentStatus = DocumentStatusEnum.Open; ConfirmationStatus = ConfirmationStatusEnum.待确认;
        Capabilities = new AfterSalesCapabilitiesDto { CanSave = true, CanEditFacts = true, CanVoid = true };
        SelectedLine = null;
        _loading = false; UpdateDirtyState();
    }

    private async Task LoadAsync(Guid uid, CancellationToken outerToken)
    {
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken); _loadCancellation = cts;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetAsync(uid, cts.Token), cts.Token, "加载售后服务单失败");
            if (cts.IsCancellationRequested || _disposed || !ReferenceEquals(_loadCancellation, cts)) return;
            if (!result.Success || result.Data is null) { HandleFailure("加载失败", result); return; }
            ApplyDetail(result.Data);
            await LoadLookupsAsync(cts.Token);
            await LoadProductImagesAsync(cts.Token);
        }, "正在加载售后服务单...");
        if (ReferenceEquals(_loadCancellation, cts)) _loadCancellation = null;
        cts.Dispose();
    }

    private async Task RefreshAfterSuccessAsync(Guid uid, string success)
    {
        _refreshOnlyUid = uid;
        NotifyPendingState();
        if (await ReloadCoreAsync(uid, CancellationToken.None))
        {
            _refreshOnlyUid = null;
            NotifyPendingState();
            ResultSummary = success;
            _messages.ShowSuccess(success, DocumentTitle);
        }
    }
    private async Task<bool> ReloadCoreAsync(Guid uid, CancellationToken token)
    {
        var result = await _runner.RunAsync(() => _api.GetAsync(uid, token), token, "刷新售后服务单失败");
        if (result.Success && result.Data is not null)
        {
            ApplyDetail(result.Data);
            await LoadProductImagesAsync(token);
            return true;
        }
        _messages.ShowWarning("操作已完成，刷新失败", result.Error?.Message ?? result.Message);
        return false;
    }

    private void ApplyDetail(AfterSalesDetailDto detail)
    {
        _loading = true; _hasServerIdentity = true;
        _detailVersion++; EndProductSelection();
        Header.Load(detail.Header);
        LineList.LoadFromDtos(detail.Lines);
        SelectedLine = Lines.FirstOrDefault(); DocumentStatus = detail.Header.DocumentStatus;
        ConfirmationStatus = detail.Header.ConfirmationStatus; Capabilities = detail.Capabilities;
        SynchronizeCachedSelections();
        Title = DocumentTitle; IsReadOnly = !HasAccess; _loading = false;
        IsDirty = false; OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCommands();
        _navigation.UpdateDocumentKey(this, $"after-sales:{Header.Uid:N}");
    }

    // 写入成功后先接纳服务端返回的身份、版本和状态，避免随后刷新失败时误走重复创建。
    private void ApplyCommandResult(AfterSalesCommandResult result)
    {
        _loading = true;
        _hasServerIdentity = true;
        Header.Uid = result.Uid;
        Header.Version = result.Version;
        Header.AfterSalesNo = result.AfterSalesNo;
        DocumentStatus = result.DocumentStatus;
        ConfirmationStatus = result.ConfirmationStatus;
        Capabilities = result.Capabilities;
        Title = DocumentTitle;
        _loading = false;
        OnPropertyChanged(nameof(IsPersisted));
        OnPropertyChanged(nameof(DocumentTitle));
    }

    // 服务端行字段路径使用 Lines[Uid].Field；定位失败时仍保留整体错误摘要。
    private void SelectErrorLine(string field)
    {
        const string prefix = "Lines[";
        if (!field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var end = field.IndexOf(']', prefix.Length);
        if (end <= prefix.Length || !Guid.TryParse(field[prefix.Length..end], out var uid)) return;
        SelectedLine = Lines.FirstOrDefault(line => line.Uid == uid);
    }

    private async Task LoadLookupsAsync(CancellationToken token)
    {
        if (_disposed) return;
        _ = await _referenceDataCache.EnsureLoadedAsync();
        if (token.IsCancellationRequested || _disposed) return;
        SynchronizeCachedSelections();

        var kinds = new[] { "owners", "sales-order-lines" };
        foreach (var kind in kinds)
        {
            var result = await _runner.RunAsync(() => _api.LookupAsync(kind, new AfterSalesLookupRequest
            { CustomerId = Header.CustomerId == Guid.Empty ? null : Header.CustomerId, PageIndex = 1, PageSize = 200 }, token), token, "加载售后候选失败");
            if (token.IsCancellationRequested || _disposed) return;
            var target = kind == "owners" ? Owners : SalesOrderLines;
            if (!result.Success || result.Data is null) continue;
            target.Clear();
            foreach (var item in result.Data.Items) target.Add(item);
        }
        await LoadQualityCategoriesAsync(token);
    }

    /// <summary>显式刷新分类，不改写单据的原分类身份或其他未保存输入。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshLookups), AllowConcurrentExecutions = true)]
    private Task RefreshQualityCategoriesAsync() => CanRefreshLookups()
        ? LoadQualityCategoriesAsync(CancellationToken.None) : Task.CompletedTask;

    /// <summary>读取全树并取消旧请求；只有最新请求且页面仍存活时接纳候选。</summary>
    private async Task LoadQualityCategoriesAsync(CancellationToken token)
    {
        if (_disposed || !HasAccess) return;
        _categoryCancellation?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _categoryCancellation = cts;
        IsQualityCategoryLoading = true;
        try
        {
            var result = await _runner.RunAsync(() => _qualityCategoryApi.GetAllAsync(cts.Token), cts.Token, "加载品质分类失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_categoryCancellation, cts)) return;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = result.Error?.Message ?? result.Message;
                return;
            }
            var tree = new AfterSalesQualityCategoryTree();
            tree.Load(result.Data);
            QualityCategories = tree.Roots.ToArray();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 新请求或页面释放导致的取消不覆盖当前选择和反馈。
        }
        catch (InvalidOperationException)
        {
            if (!_disposed && ReferenceEquals(_categoryCancellation, cts))
                ResultSummary = "品质分类层级无效，原分类选择已保留，请联系管理员检查。";
        }
        finally
        {
            if (ReferenceEquals(_categoryCancellation, cts))
            {
                _categoryCancellation = null;
                if (!_disposed) IsQualityCategoryLoading = false;
            }
            cts.Dispose();
        }
    }

    private void SynchronizeCachedSelections()
    {
        SelectedCustomer = Header.CustomerId == Guid.Empty
            ? null
            : Customers.FirstOrDefault(item => item.Uid == Header.CustomerId)
              ?? new CustomerDto
              {
                  Uid = Header.CustomerId,
                  CustomerCode = Header.CustomerCodeSnapshot,
                  CustomerName = Header.CustomerNameSnapshot,
              };

        Header.SynchronizeSupplier(Header.ResponsibleSupplierId.HasValue
            ? Suppliers.FirstOrDefault(x => x.Uid == Header.ResponsibleSupplierId.Value)
              ?? new SupplierDto { Uid = Header.ResponsibleSupplierId.Value }
            : null);
    }

    // 请求序号确保较慢的旧查询不覆盖当前候选；既有产品接口不支持取消参数。
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var version = ++_productSearchVersion;
        ProductCandidates.Clear(); SelectedProductCandidate = null;
        if (_disposed || !CanEditFacts) return;
        IsProductSearchBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _productApi.SearchLookupAsync(new ProductLookupRequest
            { Keyword = keyword?.Trim() ?? string.Empty, Limit = 30 }), "查询产品失败");
            if (_disposed || version != _productSearchVersion || !CanEditFacts) return;
            if (result.Success && result.Data is not null)
                foreach (var product in result.Data) ProductCandidates.Add(product);
        }
        finally { if (version == _productSearchVersion) IsProductSearchBusy = false; }
    }

    // 产品控件只回填当前问题或补件，图片不进入售后写入契约。
    [RelayCommand]
    private void CommitIssueProduct()
    {
        if (!CanEditFacts || SelectedProductCandidate is not { } product) return;
        product.Adapt(Header);
        EndProductSelection();
    }

    [RelayCommand]
    private void ClearIssueProduct()
    {
        if (!CanEditFacts) return;
        Header.IssueProductId = null; Header.IssueSkuSnapshot = string.Empty;
        Header.IssueLinkSkuSnapshot = string.Empty; Header.IssueCustomerSkuSnapshot = string.Empty; Header.ImagePath = null;
        EndProductSelection();
    }

    /// <summary>选择标准补件时采用产品单位快照，保留稳定补件行身份。</summary>
    [RelayCommand]
    private void CommitProduct(AfterSalesLineModel? line)
    {
        if (!CanEditFacts || line is null || !Lines.Contains(line) || SelectedProductCandidate is not { } product) return;
        product.Adapt(line);
        line.UnitSnapshot = product.Unit?.Trim() ?? string.Empty;
        line.IsNonStandard = false;
        EndProductSelection();
    }

    /// <summary>清除标准补件身份及其单位，后续非标单位须明确填写。</summary>
    [RelayCommand]
    private void ClearProduct(AfterSalesLineModel? line)
    {
        if (!CanEditFacts || line is null || !Lines.Contains(line)) return;
        line.ProductId = null; line.SkuSnapshot = string.Empty; line.LinkSkuSnapshot = string.Empty;
        line.CustomerSkuSnapshot = string.Empty; line.DescriptionSnapshot = string.Empty;
        line.ColorId = null; line.ColorSnapshot = string.Empty; line.SpecificationSnapshot = string.Empty; line.UnitSnapshot = string.Empty; line.ImagePath = null;
        EndProductSelection();
    }

    private void EndProductSelection()
    {
        _productSearchVersion++; IsProductSearchBusy = false;
        SelectedProductCandidate = null; ProductCandidates.Clear();
    }

    // 重新打开时只回填当前产品图片，不覆盖投诉型号等历史快照，不标记业务修改。
    private async Task LoadProductImagesAsync(CancellationToken token)
    {
        var version = _detailVersion;
        var ids = Lines.Select(x => x.ProductId).Append(Header.IssueProductId)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        foreach (var id in ids)
        {
            if (token.IsCancellationRequested || _disposed || version != _detailVersion) return;
            var result = await _runner.RunAsync(() => _productApi.GetByIdAsync(id), "读取产品图片失败");
            if (token.IsCancellationRequested || _disposed || version != _detailVersion) return;
            if (!result.Success || result.Data is null) continue;
            if (Header.IssueProductId == id) Header.ImagePath = result.Data.MainImage;
            foreach (var line in Lines.Where(x => x.ProductId == id)) line.ImagePath = result.Data.MainImage;
        }
    }

    private void OnLineListChanged(object? sender, ListStateChangedEventArgs<AfterSalesLineModel> args)
    {
        if (_loading) return;
        if (args.PropertyName == nameof(AfterSalesLineModel.ColorId) && args.Line is { } line)
            line.ColorSnapshot = Colors.FirstOrDefault(x => x.Uid == line.ColorId)?.ColorNameZh ?? string.Empty;
        UpdateDirtyState();
    }
    private void OnEditChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) UpdateDirtyState(); }
    private void UpdateDirtyState()
    {
        IsDirty = Header.IsDirty || Lines.Any(x => x.IsNew ? !x.IsEmptyPlaceholder : x.IsDirty) || LineList.DeletedItems.Count > 0;
        OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCommands();
    }
    private bool CanAddLine() => CanEditFacts && IsNotBusy;
    private bool CanDeleteLine() => CanEditFacts && IsNotBusy && SelectedLine is not null;
    private bool CanReload() => HasAccess && IsNotBusy && IsPersisted && !HasPendingIntent;
    private bool CanRefreshLookups() => HasAccess && IsNotBusy && !HasPendingIntent;
    private bool CanSave() => CanMaintain && IsNotBusy && (_pendingWrite?.Operation == "save"
        || !HasPendingIntent && !HasInputErrors && Capabilities.CanSave && IsDirty);
    private bool CanConfirm() => CanConfirmDemand && IsNotBusy && (_pendingConfirmation is not null
        || CanMaintain && !HasPendingIntent && !HasInputErrors && DocumentStatus == DocumentStatusEnum.Open
        && ConfirmationStatus != ConfirmationStatusEnum.已确认 && (Capabilities.CanConfirm || IsDirty));
    private bool CanCancelConfirmation() => CanCancelDemand && IsNotBusy && (_pendingWrite?.Operation == "cancel"
        || !HasPendingIntent && Capabilities.CanCancelConfirmation && !IsDirty);
    private bool CanCloseDocument() => CanManageLifecycle && IsNotBusy && (_pendingWrite?.Operation == "close"
        || !HasPendingIntent && Capabilities.CanClose && !IsDirty);
    private bool CanReopen() => CanManageLifecycle && IsNotBusy && (_pendingWrite?.Operation == "reopen"
        || !HasPendingIntent && Capabilities.CanReopen && !IsDirty);
    private bool CanVoid() => CanManageLifecycle && IsNotBusy && (_pendingWrite?.Operation == "void"
        || !HasPendingIntent && Capabilities.CanVoid && !IsDirty);
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ConfirmDemandCommand.NotifyCanExecuteChanged();
        CancelDemandConfirmationCommand.NotifyCanExecuteChanged(); CloseCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged(); VoidCommand.NotifyCanExecuteChanged();
        AddLineCommand.NotifyCanExecuteChanged(); DeleteLineCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged(); RefreshLookupsCommand.NotifyCanExecuteChanged();
        RefreshQualityCategoriesCommand.NotifyCanExecuteChanged();
        RetryPendingWriteCommand.NotifyCanExecuteChanged(); RefreshCurrentFactCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消可替代读取并解除行订阅；重复释放安全。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        PropertyChanged -= OnBusyPropertyChanged;
        _categoryCancellation?.Cancel();
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null;
        Header.PropertyChanged -= OnEditChanged;
        LineList.ListStateChanged -= OnLineListChanged;
        LineList.Clear(); CommitPendingEdits = null;
        _productSearchVersion++; _detailVersion++;
    }
}
