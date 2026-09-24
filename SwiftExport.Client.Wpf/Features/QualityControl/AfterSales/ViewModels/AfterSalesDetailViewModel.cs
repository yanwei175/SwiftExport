using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Mapster;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

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
    private int _productSearchVersion;
    private int _detailVersion;
    private readonly Dictionary<string, (string Fingerprint, Guid CommandId)> _pendingCommands = [];
    private PendingDemandConfirmation? _pendingConfirmation;
    private CancellationTokenSource? _loadCancellation;
    private bool _hasServerIdentity;
    private bool _loading;
    private bool _disposed;

    /// <summary>创建售后详情文档并接入业务命令、对话框、导航、权限及基础资料缓存。</summary>
    public AfterSalesDetailViewModel(IAfterSalesApi api, IAfterSalesFeatureService feature, IApiCallRunner runner,
        INavigationService navigation, IDialogService dialogs, IAfterSalesReasonDialogService reasonDialog,
        IMessageService messages, ICurrentUserService currentUser, IReferenceDataCacheService referenceDataCache, IProductApi productApi)
    {
        _api = api; _feature = feature; _runner = runner; _navigation = navigation; _dialogs = dialogs;
        _reasonDialog = reasonDialog; _messages = messages; _currentUser = currentUser;
        _referenceDataCache = referenceDataCache;
        _productApi = productApi;
        Title = "售后服务单-新建";
        Header.PropertyChanged += OnEditChanged;
        LineList.ListStateChanged += OnLineListChanged;
        RegisterBusyAwareCommand(SaveCommand); RegisterBusyAwareCommand(ConfirmDemandCommand);
        RegisterBusyAwareCommand(CancelDemandConfirmationCommand); RegisterBusyAwareCommand(CloseCommand);
        RegisterBusyAwareCommand(ReopenCommand); RegisterBusyAwareCommand(VoidCommand);
        RegisterBusyAwareCommand(AddLineCommand); RegisterBusyAwareCommand(DeleteLineCommand);
        RegisterBusyAwareCommand(ReloadCommand); RegisterBusyAwareCommand(RefreshLookupsCommand);
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
    public ObservableCollection<AfterSalesLookupDto> QualityCategories { get; } = [];
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.AfterSalesNo) ? "售后服务单-新建" : Header.AfterSalesNo;
    public bool HasUnsavedChanges => IsDirty || _pendingConfirmation is not null;
    public bool IsPersisted => _hasServerIdentity;
    public bool HasAccess => _currentUser.HasPermission(PermissionKeys.ModuleQualityControlAccess);
    public string DocumentStatusText => DocumentStatus switch { DocumentStatusEnum.Closed => "已结案", DocumentStatusEnum.Voided => "已作废", _ => "开放" };
    public string ConfirmationStatusText => ConfirmationStatus == ConfirmationStatusEnum.已确认 ? "实物需求已确认" : "实物需求待确认";
    public bool CanEditSupplement => Capabilities.CanSave && HasAccess && _pendingConfirmation is null;
    public bool CanEditFacts => Capabilities.CanEditFacts && HasAccess && _pendingConfirmation is null;

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
        if (_pendingConfirmation is not null)
        {
            _messages.ShowWarning("售后确认结果未定", "请使用原请求重试确认，取得确定结果后再关闭页签。");
            return false;
        }
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭售后服务单", "当前服务单有未保存修改，确定放弃并关闭吗？", "放弃并关闭", "继续编辑", cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddLine))] private void AddLine()
    {
        var line = LineList.AddNew(new() { Uid = GuidSequentialGenerator.NewGuid(), Version = 1,
            LineNumber = LineList.AllLines.Count == 0 ? 1 : LineList.AllLines.Max(x => x.LineNumber) + 1 });
        SelectedLine = line; UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanDeleteLine))] private void DeleteLine()
    {
        var line = SelectedLine!;
        LineList.Remove(line); SelectedLine = Lines.FirstOrDefault(); UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private async Task SaveAsync() => await RunWriteAsync(async () =>
    {
        if (!Validate(false)) return;
        var create = !IsPersisted;
        var command = CreateSaveCommand("save");
        var result = await _runner.RunAsync(() => create ? _api.CreateAsync(command) : _api.SaveAsync(Header.Uid, command), "保存售后服务单失败");
        CompleteCommand("save", result);
        if (!result.Success || result.Data is null) { HandleFailure("保存失败", result); return; }
        ApplyCommandResult(result.Data);
        await ReloadAfterWriteAsync(result.Data.Uid, "售后服务单已保存");
    }, "正在保存售后服务单...");

    [RelayCommand(CanExecute = nameof(CanConfirm))] private async Task ConfirmDemandAsync()
    {
        if (_pendingConfirmation is null)
        {
            if (!Validate(true)) return;
            if (!Lines.Any(x => x.Quantity > 0)) { _messages.ShowInfo("无需确认", "当前没有正数量补发要求，投诉单可以直接保存并在处理完成后结案。"); return; }
            if (!await _dialogs.ConfirmAsync("确认实物需求", "将先保存当前修改，再把正数量补件交给来源需求。确认后相关事实会锁定。", "保存并确认")) return;
            SetPendingConfirmation(new PendingDemandConfirmation(CreateSaveCommand("confirm-save"), !IsPersisted, Guid.NewGuid()));
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
                        _pendingCommands.Remove("confirm-save");
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
                    _pendingCommands.Remove("confirm-save");
                    SetPendingConfirmation(null);
                    await ReloadCoreAsync(pending.Save.Uid, CancellationToken.None);
                }
                HandleFailure("服务单已保存，但实物需求未确认", confirmed);
                return;
            }
            _pendingCommands.Remove("confirm-save");
            SetPendingConfirmation(null);
            await ReloadAfterWriteAsync(pending.Save.Uid, "实物需求已确认");
        }, "正在保存并确认实物需求...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))] private async Task CancelDemandConfirmationAsync()
    {
        var reason = await _reasonDialog.RequestAsync("取消实物需求确认", "来源需求会先检查是否能够安全撤回；失败时服务单仍保持确认和锁定状态。");
        if (reason is null) return;
        await ExecuteCommandAsync("cancel", id => _api.CancelDemandConfirmationAsync(Header.Uid,
            new CancelAfterSalesDemandConfirmationCommand { CommandId = id, Version = Header.Version, Reason = reason }), "已取消实物需求确认", reason);
    }
    [RelayCommand(CanExecute = nameof(CanCloseDocument))] private async Task CloseAsync()
    {
        var reason = await _reasonDialog.RequestAsync("结案", "正数量要求必须已正式交付完成或由下游合法关闭。请输入结案说明。");
        if (reason is null) return;
        await ExecuteCommandAsync("close", id => _api.CloseAsync(Header.Uid, new CloseAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已结案", reason);
    }
    [RelayCommand(CanExecute = nameof(CanReopen))] private async Task ReopenAsync()
    {
        var reason = await _reasonDialog.RequestAsync("重开售后服务单", "重开只恢复售后生命周期，不会重新发布来源需求。请输入原因。");
        if (reason is null) return;
        await ExecuteCommandAsync("reopen", id => _api.ReopenAsync(Header.Uid, new ReopenAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已重开", reason);
    }
    [RelayCommand(CanExecute = nameof(CanVoid))] private async Task VoidAsync()
    {
        var reason = await _reasonDialog.RequestAsync("作废售后服务单", "作废要求服务单开放、未确认且没有活动来源需求。请输入原因。");
        if (reason is null) return;
        await ExecuteCommandAsync("void", id => _api.VoidAsync(Header.Uid, new VoidAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已作废", reason);
    }
    [RelayCommand(CanExecute = nameof(CanReload))] private async Task ReloadAsync()
    {
        if (_pendingConfirmation is not null) return;
        if (IsDirty && !await _dialogs.ConfirmAsync("重新加载售后服务单", "重新加载会放弃当前未保存修改，确定继续吗？", "放弃并重新加载", "继续编辑")) return;
        await LoadAsync(Header.Uid, CancellationToken.None);
    }
    [RelayCommand(CanExecute = nameof(CanRefreshLookups))] private Task RefreshLookupsAsync() => LoadLookupsAsync(CancellationToken.None);

    // 生命周期命令共享写入门禁，服务端结构化错误保留当前编辑状态。
    private async Task ExecuteCommandAsync(string operation, Func<Guid, Task<ServiceResult<AfterSalesCommandResult>>> call, string success, string reason)
    {
        await RunWriteAsync(async () =>
        {
            var id = CommandId(operation, JsonSerializer.Serialize(new { Header.Uid, Header.Version, reason }));
            var result = await _runner.RunAsync(() => call(id), success + "失败");
            CompleteCommand(operation, result);
            if (!result.Success || result.Data is null) { HandleFailure(success + "失败", result); return; }
            ApplyCommandResult(result.Data);
            await ReloadAfterWriteAsync(Header.Uid, success);
        }, "正在执行售后状态操作...");
    }

    // 本地验证只负责明显输入错误，状态及跨领域资格仍由后端决定。
    private bool Validate(bool forConfirmation)
    {
        if (CommitPendingEdits?.Invoke() == false) return ValidationFailed("请先完成或修正补件表格中的输入。");
        if (HasInputErrors) return ValidationFailed("请先修正页面中无效的数字或日期。");
        if (Header.CustomerId == Guid.Empty) return ValidationFailed("Header.CustomerId：请选择客户。");
        if (Header.AcceptedDate == default) return ValidationFailed("Header.AcceptedDate：请选择受理日期。");
        var headerError=Header.Validate(forConfirmation && Lines.Any(x=>x.Quantity>0));
        if(headerError is not null) return ValidationFailed(headerError);
        foreach (var line in Lines)
        {
            if(line.IsNew && line.IsEmptyPlaceholder) continue;
            var error = line.Validate(forConfirmation);
            if (error is null) continue;
            SelectedLine = line;
            return ValidationFailed($"第 {line.LineNumber} 行：{error}");
        }
        ValidationSummary = null; return true;
    }

    private bool ValidationFailed(string message) { ValidationSummary = message; _messages.ShowWarning("售后服务单未提交", message); return false; }

    private SaveAfterSalesCommand CreateSaveCommand(string operation)
    {
        var command = new SaveAfterSalesCommand
        {
            Uid = Header.Uid, Version = Header.Version,
            Header = Header.ToDto(), AddedLines = Lines.Where(x => x.IsNew && !x.IsEmptyPlaceholder).Select(x => x.ToEditDto()).ToList(),
            UpdatedLines = Lines.Where(x => !x.IsNew && x.IsDirty).Select(x => x.ToEditDto()).ToList(),
            DeletedLines = LineList.DeletedItems.Select(x => new AfterSalesDeletedLineDto { Uid = x.Uid, Version = x.Version }).ToList()
        };
        var fingerprint = JsonSerializer.Serialize(command);
        command.CommandId = CommandId(operation, fingerprint);
        return command;
    }

    private Guid CommandId(string operation, string fingerprint)
    {
        if (_pendingCommands.TryGetValue(operation, out var pending) && pending.Fingerprint == fingerprint) return pending.CommandId;
        var id = Guid.NewGuid(); _pendingCommands[operation] = (fingerprint, id); return id;
    }
    private void CompleteCommand<T>(string operation, ServiceResult<T>? result)
    {
        if (result?.Success == true || result?.Error is not null && !MustKeepPendingIntent(result.Error.Code))
            _pendingCommands.Remove(operation);
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
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEditFacts));
        OnPropertyChanged(nameof(CanEditSupplement));
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
        if (result?.Error?.Code == "VERSION_CONFLICT") _messages.ShowWarning("数据已变化", message + " 本地输入已保留，请决定是否刷新。");
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

    private async Task ReloadAfterWriteAsync(Guid uid, string success)
    {
        await ReloadCoreAsync(uid, CancellationToken.None);
        ResultSummary = success; _messages.ShowSuccess(success, DocumentTitle);
    }
    private async Task ReloadCoreAsync(Guid uid, CancellationToken token)
    {
        var result = await _runner.RunAsync(() => _api.GetAsync(uid, token), token, "刷新售后服务单失败");
        if (result.Success && result.Data is not null)
        {
            ApplyDetail(result.Data);
            await LoadProductImagesAsync(token);
        }
        else _messages.ShowWarning("操作已完成，刷新失败", result.Error?.Message ?? result.Message);
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
            var target = kind switch { "owners" => Owners,
                "sales-order-lines" => SalesOrderLines, _ => QualityCategories };
            if (!result.Success || result.Data is null) continue;
            target.Clear();
            foreach (var item in result.Data.Items) target.Add(item);
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
            ? Suppliers.FirstOrDefault(x=>x.Uid==Header.ResponsibleSupplierId.Value)
              ?? new SupplierDto { Uid=Header.ResponsibleSupplierId.Value }
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

    [RelayCommand]
    private void CommitProduct(AfterSalesLineModel? line)
    {
        if (!CanEditFacts || line is null || !Lines.Contains(line) || SelectedProductCandidate is not { } product) return;
        product.Adapt(line);
        line.IsNonStandard = false;
        EndProductSelection();
    }

    [RelayCommand]
    private void ClearProduct(AfterSalesLineModel? line)
    {
        if (!CanEditFacts || line is null || !Lines.Contains(line)) return;
        line.ProductId = null; line.SkuSnapshot = string.Empty; line.LinkSkuSnapshot = string.Empty;
        line.CustomerSkuSnapshot = string.Empty; line.DescriptionSnapshot = string.Empty;
        line.ColorId = null; line.ColorSnapshot = string.Empty; line.SpecificationSnapshot = string.Empty; line.ImagePath = null;
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
    private bool CanReload() => HasAccess && IsNotBusy && IsPersisted && _pendingConfirmation is null;
    private bool CanRefreshLookups() => HasAccess && IsNotBusy;
    private bool CanSave() => HasAccess && IsNotBusy && !HasInputErrors && Capabilities.CanSave && IsDirty && _pendingConfirmation is null;
    private bool CanConfirm() => HasAccess && IsNotBusy && (_pendingConfirmation is not null || !HasInputErrors && DocumentStatus == DocumentStatusEnum.Open
        && ConfirmationStatus != ConfirmationStatusEnum.已确认 && (Capabilities.CanConfirm || IsDirty));
    private bool CanCancelConfirmation() => HasAccess && IsNotBusy && _pendingConfirmation is null && Capabilities.CanCancelConfirmation && !IsDirty;
    private bool CanCloseDocument() => HasAccess && IsNotBusy && _pendingConfirmation is null && Capabilities.CanClose && !IsDirty;
    private bool CanReopen() => HasAccess && IsNotBusy && _pendingConfirmation is null && Capabilities.CanReopen && !IsDirty;
    private bool CanVoid() => HasAccess && IsNotBusy && _pendingConfirmation is null && Capabilities.CanVoid && !IsDirty;
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ConfirmDemandCommand.NotifyCanExecuteChanged();
        CancelDemandConfirmationCommand.NotifyCanExecuteChanged(); CloseCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged(); VoidCommand.NotifyCanExecuteChanged();
        AddLineCommand.NotifyCanExecuteChanged(); DeleteLineCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged(); RefreshLookupsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消可替代读取并解除行订阅；重复释放安全。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null;
        Header.PropertyChanged -= OnEditChanged;
        LineList.ListStateChanged -= OnLineListChanged;
        LineList.Clear(); CommitPendingEdits = null;
        _productSearchVersion++; _detailVersion++;
    }
}
