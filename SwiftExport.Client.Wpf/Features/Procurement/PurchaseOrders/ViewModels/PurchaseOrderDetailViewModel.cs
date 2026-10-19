using SwiftExport.Contracts.DTOs.Errors;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购详情文档；统一处理手工创建、草稿商务编辑、确认状态命令。</summary>
public sealed partial class PurchaseOrderDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-order-detail";
    private readonly IProcurementOrderApi _api;
    private readonly IProcurementMasterDataQueryApi _masterData;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private readonly ProcurementCommandSession _commandSession;
    private readonly IApiCallRunner _runner;
    private readonly Guid _draftIdentity = Guid.NewGuid();
    private bool _initialized;
    private bool _hadPendingWrite;
    private int _sessionVersion;
    private bool _loading;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _customerSearchCts, _supplierSearchCts, _productSearchCts, _deliverySupplierSearchCts;
    private long _loadSequence;

    public PurchaseOrderDetailViewModel(IProcurementOrderApi api, IProcurementMasterDataQueryApi masterData,
        IDialogService dialogs, IMessageService messages,
        INavigationService navigation, ICurrentUserService currentUser,
        ProcurementReversalSession? reversalSession = null, IProcurementReversalDialogService? reversalDialogs = null,
        ProcurementCommandSession? commandSession = null, IApiCallRunner? apiCallRunner = null)
    {
        _api = api; _masterData = masterData; _dialogs = dialogs;
        _messages = messages; _navigation = navigation; _currentUser = currentUser;
        _commandSession = commandSession ?? new ProcurementCommandSession();
        _runner = apiCallRunner ?? new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        _commandSession.Changed += OnCommandSessionChanged;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        _reversalSession = reversalSession ?? new ProcurementReversalSession(); _reversalDialogs = reversalDialogs;
        _reversalSession.Changed += OnReversalChanged;
        RegisterBusyAwareCommand(VoidOrderCommand); RegisterBusyAwareCommand(DeleteDraftLinesCommand); RegisterBusyAwareCommand(RetryReversalCommand);
        Title = "采购订单详情";
        Lines.ListStateChanged += OnLinesStateChanged;
        Header.PropertyChanged += OnHeaderPropertyChanged;
        RegisterBusyAwareCommand(CreateCommand); RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(RefreshCommand); RegisterBusyAwareCommand(CloseDocumentCommand);
        RegisterBusyAwareCommand(ReorderLineCommand);
        RegisterBusyAwareCommand(RetryPendingCommand);
    }

    public PurchaseOrderHeaderEditorModel Header { get; } = new();
    public PurchaseOrderLineListModel Lines { get; } = new();
    public ObservableCollection<ProcurementCustomerCandidateDto> CustomerCandidates { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> SupplierCandidates { get; } = [];
    public ObservableCollection<ProcurementProductCandidateDto> ProductCandidates { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> DeliverySupplierCandidates { get; } = [];
    public string DocumentTitle => IsCreateMode ? "新建采购" : string.IsNullOrWhiteSpace(OrderNo) ? "采购订单详情" : OrderNo;
    public bool HasUnsavedChanges => HeaderHasChanges || Lines.HasChanges || HasPendingWrite;
    private Guid WriteIdentity => IsCreateMode ? _draftIdentity : PurchaseOrderId;
    /// <summary>跨管理与详情共享的本用户未决写入。</summary>
    public bool HasPendingWrite => _commandSession.Find(ReversalUser, WriteIdentity) is not null;
    public bool HasReadPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);
    public bool HasCreatePermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate);
    public bool CanEdit => CanInteractWithDocument && !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit) && Header.CanEditDraft;
    public bool CanConfirm => CanInteractWithDocument && !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm) && Header.CanConfirm;
    public bool CanCancel => CanInteractWithDocument && !IsCreateMode
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm)
        && LifecycleStatus == PurchaseOrderLifecycleStatusEnum.Active
        && ConfirmationStatus == ConfirmationStatusEnum.已确认;

    [ObservableProperty] public partial bool IsCreateMode { get; set; }
    [ObservableProperty] public partial bool HeaderHasChanges { get; set; }
    [ObservableProperty] public partial Guid PurchaseOrderId { get; set; }
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial int Version { get; set; }
    [ObservableProperty] public partial ConfirmationStatusEnum ConfirmationStatus { get; set; }
    [ObservableProperty] public partial PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
    [ObservableProperty] public partial ProcurementCustomerCandidateDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial ProcurementSupplierCandidateDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial ProcurementProductCandidateDto? SelectedProductCandidate { get; set; }
    [ObservableProperty] public partial string CustomerLookupText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierLookupText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsCustomerSearchBusy { get; set; }
    [ObservableProperty] public partial bool IsSupplierSearchBusy { get; set; }
    [ObservableProperty] public partial bool IsProductSearchBusy { get; set; }
    [ObservableProperty] public partial bool IsDeliverySupplierSearchBusy { get; set; }
    [ObservableProperty] public partial DateTime OrderDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string CancellationReason { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "正在初始化采购详情...";
    [ObservableProperty] public partial int SelectedDetailTabIndex { get; set; }

    /// <summary>根据文档参数进入创建或新链详情，不再在详情页切换旧链。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_initialized || _reversalDisposed) return; _initialized = true;
        var request = parameter as PurchaseOrderDocumentOpenRequest;
        if (request is null || request.PurchaseOrderId == Guid.Empty)
        {
            if (!HasCreatePermission) { StatusText = "当前账号没有创建采购订单的权限。"; return; }
            InitializeCreate(); return;
        }
        if (!HasReadPermission) { StatusText = "当前账号没有读取采购订单的权限。"; return; }
        await LoadAsync(request.PurchaseOrderId, cancellationToken);
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
        => !IsBusy && !HasPendingWrite && _reversalSession.Find(ReversalUser, PurchaseOrderId) is null
            && (!HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭采购详情", "存在未保存修改，确定放弃吗？", "放弃并关闭", "继续编辑", cancellationToken));

    [RelayCommand]
    private void AddLine()
    {
        if (!IsCreateMode || !CanInteractWithDocument) return;
        Lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = Lines.Items.Count + 1, Quantity = 1m });
        NotifyState();
    }

    [RelayCommand]
    private void RemoveLine(PurchaseOrderLineEditModel? line)
    {
        if (!IsCreateMode || !CanInteractWithDocument || line is null || !line.IsNew) return;
        Lines.Remove(line); NotifyState();
    }

    /// <summary>仅创建态且具备创建权限时允许重排；已保存采购明细维持原有编辑边界。</summary>
    public bool CanReorder => HasCreatePermission && IsCreateMode && IsNotBusy && CanInteractWithDocument
        && RowReorderRequest.IsViewInSourceOrder(CollectionViewSource.GetDefaultView(Lines.Items));

    /// <summary>按目标行锚点移动新增采购行，并以完整集合顺序重编行号。</summary>
    [RelayCommand(CanExecute = nameof(CanReorderLine))]
    private void ReorderLine(RowReorderRequest? request)
    {
        if (!CanReorderLine(request) || !request!.TryResolve(Lines.Items, out var line, out var index)) return;
        using (Lines.SuppressListEvents())
        {
            if (!Lines.Move(line, index)) return;
            for (var i = 0; i < Lines.Items.Count; i++) Lines.Items[i].LineNumber = i + 1;
        }
        Lines.SelectedItem = line;
        NotifyState();
    }

    private bool CanReorderLine(RowReorderRequest? request) => CanReorder && request is not null
        && request.TryResolve(Lines.Items, out var line, out _) && line is { IsNew: true, IsDeleted: false };

    /// <summary>基类忙碌和只读变化同步创建态重排手柄。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) NotifyState();
    }

    [RelayCommand]
    private Task SearchCustomersAsync(string? keyword) => SearchCandidatesAsync(keyword, CustomerCandidates,
        (request, ct) => _masterData.SearchCustomersAsync(request, ct), () => _customerSearchCts, value => _customerSearchCts = value, value => IsCustomerSearchBusy = value);
    [RelayCommand]
    private Task SearchSuppliersAsync(string? keyword) => SearchCandidatesAsync(keyword, SupplierCandidates,
        (request, ct) => _masterData.SearchSuppliersAsync(request, ct), () => _supplierSearchCts, value => _supplierSearchCts = value, value => IsSupplierSearchBusy = value);
    [RelayCommand]
    private Task SearchDeliverySuppliersAsync(string? keyword) => SearchCandidatesAsync(keyword, DeliverySupplierCandidates,
        (request, ct) => _masterData.SearchSuppliersAsync(request, ct), () => _deliverySupplierSearchCts, value => _deliverySupplierSearchCts = value, value => IsDeliverySupplierSearchBusy = value);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var current = ReplaceCancellation(ref _productSearchCts);
        ProductCandidates.Clear();
        SelectedProductCandidate = null;
        IsProductSearchBusy = true;
        try
        {
            var result = await _masterData.SearchProductsAsync(new ProcurementProductSearchRequest
            {
                Keyword = keyword?.Trim(),
                PageIndex = 1,
                PageSize = 50,
                CustomerId = SelectedCustomer?.Uid,
                SupplierId = SelectedSupplier?.Uid
            }, current.Token);
            if (!_reversalDisposed && !current.IsCancellationRequested && ReferenceEquals(_productSearchCts, current))
                ReplaceItems(ProductCandidates, result.Success ? result.Data?.Items : null);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_productSearchCts, current)) { _productSearchCts = null; IsProductSearchBusy = false; } current.Dispose(); }
    }

    [RelayCommand] private void CommitCustomer(ProcurementCustomerCandidateDto? value) { if (value is not null) { SelectedCustomer = value; CustomerLookupText = $"{value.CustomerCode} {value.CustomerName}".Trim(); } }
    [RelayCommand] private void ClearCustomer() { SelectedCustomer = null; CustomerLookupText = string.Empty; }
    [RelayCommand] private void CommitSupplier(ProcurementSupplierCandidateDto? value) { if (value is not null) { SelectedSupplier = value; SupplierLookupText = $"{value.SupplierCode} {value.SupplierName}".Trim(); } }
    [RelayCommand] private void ClearSupplier() { SelectedSupplier = null; SupplierLookupText = string.Empty; }
    /// <summary>把 Lookup 暂存候选显式提交到当前新增行，避免依赖 DataGrid 编辑绑定的更新时间。</summary>
    [RelayCommand]
    private void CommitProduct(PurchaseOrderLineEditModel? line)
    {
        if (line is not { IsNew: true } || SelectedProductCandidate is null) return;
        line.ApplyProduct(SelectedProductCandidate);
        SelectedProductCandidate = null;
        ProductCandidates.Clear();
        NotifyState();
    }

    /// <summary>清除当前新增行的产品选择及页面级候选暂存。</summary>
    [RelayCommand]
    private void ClearProduct(PurchaseOrderLineEditModel? line)
    {
        line?.ClearProductSelection();
        SelectedProductCandidate = null;
        ProductCandidates.Clear();
        NotifyState();
    }
    [RelayCommand] private static void CommitDeliverySupplier(PurchaseOrderLineEditModel? line) { _ = line?.DeliverySupplier; }
    [RelayCommand] private static void ClearDeliverySupplier(PurchaseOrderLineEditModel? line) => line?.ClearDeliverySupplier();

    partial void OnSelectedCustomerChanged(ProcurementCustomerCandidateDto? value) => InvalidateProductSelections();
    partial void OnSelectedSupplierChanged(ProcurementSupplierCandidateDto? value) => InvalidateProductSelections();

    /// <summary>采购主体变化后清除候选及创建行选择，避免旧关系字段继续冒充当前有效快照。</summary>
    private void InvalidateProductSelections()
    {
        ProductCandidates.Clear();
        if (!IsCreateMode || _loading) return;
        foreach (var line in Lines.Items.Where(static line => line.IsNew)) line.ClearProductSelection();
    }

    /// <summary>执行可替代的远程候选查询，只接纳当前请求结果。</summary>
    private async Task SearchCandidatesAsync<T>(string? keyword, ObservableCollection<T> target,
        Func<PagedRequestBase, CancellationToken, Task<ServiceResult<PagedResponseBase<T>>>> query,
        Func<CancellationTokenSource?> getCurrent, Action<CancellationTokenSource?> setCurrent, Action<bool> setBusy)
    {
        var previous = getCurrent(); previous?.Cancel(); previous?.Dispose();
        var current = new CancellationTokenSource(); setCurrent(current); setBusy(true);
        try
        {
            var result = await query(new PagedRequestBase { Keyword = keyword?.Trim(), PageIndex = 1, PageSize = 50 }, current.Token);
            if (!_reversalDisposed && !current.IsCancellationRequested && ReferenceEquals(getCurrent(), current)) ReplaceItems(target, result.Success ? result.Data?.Items : null);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally { if (ReferenceEquals(getCurrent(), current)) { setCurrent(null); setBusy(false); } current.Dispose(); }
    }

    private static CancellationTokenSource ReplaceCancellation(ref CancellationTokenSource? field)
    { field?.Cancel(); field?.Dispose(); field = new CancellationTokenSource(); return field; }
    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T>? items)
    { target.Clear(); if (items is not null) foreach (var item in items) target.Add(item); }

    /// <summary>创建、保存和确认共用页面级 Busy，任何提交都先冻结完整负载。</summary>
    [RelayCommand(CanExecute = nameof(CanCreateCommand))]
    private Task CreateAsync() => CanCreateCommand() ? ExecuteDocumentCommandAsync(ProcurementCommandKind.Create) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanSaveCommand))]
    private Task SaveAsync() => CanSaveCommand() ? ExecuteDocumentCommandAsync(ProcurementCommandKind.Save) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanConfirmCommand))]
    private Task ConfirmAsync() => CanConfirmCommand() ? ExecuteDocumentCommandAsync(ProcurementCommandKind.Confirm) : Task.CompletedTask;

    /// <summary>最外层持有页面写入门禁，校验后冻结输入，提交期间不接纳另一种写操作。</summary>
    private async Task ExecuteDocumentCommandAsync(ProcurementCommandKind kind)
    {
        var uid = WriteIdentity; var user = ReversalUser; var session = _sessionVersion;
        await RunWriteAsync(async () =>
        {
            if (kind != ProcurementCommandKind.Confirm)
            {
                var error = await Lines.ValidateAllAsync();
                if (error is not null || Lines.Items.Count == 0 || (kind == ProcurementCommandKind.Create && (SelectedCustomer is null || SelectedSupplier is null)))
                { _messages.ShowWarning("采购信息不完整", error ?? "请选择客户、实际供应商并维护采购行。"); return; }
            }
            if (_reversalDisposed || user != ReversalUser || session != _sessionVersion || HasPendingWrite) return;
            bool frozen;
            if (kind == ProcurementCommandKind.Create)
            {
                var request = new CreateIndependentPurchaseOrderCommand
                {
                    CommandId = Guid.NewGuid(), CustomerId = SelectedCustomer!.Uid, SupplierId = SelectedSupplier!.Uid,
                    OrderDate = OrderDate, Remark = Header.Remark, PackagingRequirements = Header.PackagingRequirements,
                    Lines = Lines.Items.Select(line =>
                    {
                        var dto = line.Adapt<CreateIndependentPurchaseOrderLineCommand>();
                        dto.DeliverySupplierId = line.RequiresDeliverySupplier ? line.DeliverySupplierId : null; return dto;
                    }).ToList()
                };
                frozen = _commandSession.Freeze(user, uid, kind, request);
            }
            else if (kind == ProcurementCommandKind.Save)
            {
                frozen = _commandSession.Freeze(user, uid, kind, new UpdatePurchaseOrderDraftCommand
                {
                    CommandId = Guid.NewGuid(), ExpectedVersion = Version, Remark = Header.Remark,
                    PackagingRequirements = Header.PackagingRequirements, DiscountRemark = Header.DiscountRemark,
                    Lines = Lines.Items.Select(line => line.Adapt<UpdatePurchaseOrderDraftLineCommand>()).ToList()
                });
            }
            else
            {
                var context = await _runner.RunAsync(() => _api.GetConfirmationContextAsync(PurchaseOrderId), "读取确认依据失败");
                if (!Handle(context.ToClientResult(), "读取确认依据失败")) return;
                if (!await _dialogs.ConfirmAsync("确认采购订单", $"订单 {OrderNo} 共 {Lines.Items.Count} 行，确认后锁定采购并允许入库，是否继续？", "确认")) return;
                if (_reversalDisposed || user != ReversalUser || session != _sessionVersion || !_currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm) || HasPendingWrite) return;
                frozen = _commandSession.Freeze(user, uid, kind, new ConfirmProcurementOrderCommand
                { CommandId = Guid.NewGuid(), ExpectedVersion = context.Data!.Version, ContextFingerprint = context.Data.Fingerprint });
            }
            if (!frozen) return;
            var result = await _commandSession.SendAsync(user, uid, _api, _runner);
            if (_reversalDisposed || user != ReversalUser || session != _sessionVersion) return;
            if (!Handle(result.ToClientResult(), "采购提交未完成")) return;
            await AcceptWriteResultAsync(result.Data!);
        }, "正在提交采购...");
    }

    private bool CanRetryPending() => !_reversalDisposed && IsNotBusy
        && !_commandSession.IsSending(ReversalUser, WriteIdentity)
        && _commandSession.Find(ReversalUser, WriteIdentity) is { } pending
        && _currentUser.HasPermission(pending.Kind switch
        { ProcurementCommandKind.Confirm => PermissionKeys.ProcurementOrderConfirm, ProcurementCommandKind.Save => PermissionKeys.ProcurementOrderEdit, _ => PermissionKeys.ProcurementOrderCreate });

    /// <summary>未决写入只能使用冻结负载重试，页面值变化不参与重新构造。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryPending))]
    private async Task RetryPendingAsync()
    {
        if (!CanRetryPending()) return;
        var uid = WriteIdentity; var user = ReversalUser; var session = _sessionVersion;
        await RunWriteAsync(async () =>
        {
            var result = await _commandSession.SendAsync(user, uid, _api, _runner);
            if (_reversalDisposed || user != ReversalUser || session != _sessionVersion) return;
            if (Handle(result.ToClientResult(), "原采购命令尚未完成")) await AcceptWriteResultAsync(result.Data!);
        }, "正在重试原采购命令...");
    }

    /// <summary>成功回执先冻结旧详情；重新查询失败时保持只读，刷新恢复后才允许下一次编辑。</summary>
    private async Task AcceptWriteResultAsync(ProcurementOrderCommandResult result)
    {
        var wasCreate = IsCreateMode;
        PurchaseOrderId = result.PurchaseOrderId; OrderNo = result.OrderNo ?? OrderNo;
        Version = result.Version; ConfirmationStatus = result.ConfirmationStatus; LifecycleStatus = result.LifecycleStatus;
        IsReadOnly = true; IsCreateMode = false; HeaderHasChanges = false; Lines.AcceptChanges();
        if (wasCreate) _navigation.UpdateDocumentKey(this, $"purchase-order:new:{result.PurchaseOrderId:N}");
        if (!await LoadAsync(result.PurchaseOrderId, CancellationToken.None))
            StatusText = "采购操作已成功，详情刷新失败；当前只读，请刷新恢复，不要重复提交。";
        _messages.ShowSuccess("采购操作已成功", OrderNo);
    }
    [RelayCommand(CanExecute = nameof(CanCancelCommand))]
    private Task CancelConfirmationAsync() => StartReversalAsync(ProcurementReversalOperation.CancelConfirmation);

    [RelayCommand] private async Task RefreshAsync()
    {
        if (_reversalDisposed || PurchaseOrderId == Guid.Empty || IsBusy) return;
        if (_commandSession.Find(ReversalUser, WriteIdentity) is { Kind: not ProcurementCommandKind.Confirm })
        { StatusText = "保存或创建结果未知，只能重试冻结原命令，不能刷新覆盖原输入。"; return; }
        if (!HasPendingWrite && HasUnsavedChanges && !await _dialogs.ConfirmAsync("刷新采购", "放弃未保存修改并重新加载？", "放弃并刷新")) return;
        await RunBusyAsync(async () => { await LoadAsync(PurchaseOrderId, CancellationToken.None); }, "正在核实采购详情...");
    }
    [RelayCommand] private Task CloseDocumentAsync() => _navigation.CloseAsync(_navigation.OpenTabs.FirstOrDefault(x => ReferenceEquals(x.ContentViewModel, this)));

    private void InitializeCreate()
    {
        IsCreateMode = true; OrderDate = DateTime.Today; Header.LoadCreate(); Lines.Clear(); AddLine();
        HeaderHasChanges = false; StatusText = "请选择客户、实际供应商并维护采购行。"; NotifyState();
    }

    /// <summary>只接纳当前会话及请求的详情；失败保留旧数据和只读恢复状态，所有路径释放查询资源。</summary>
    private async Task<bool> LoadAsync(Guid uid, CancellationToken ct)
    {
        if (_reversalDisposed || !HasReadPermission) return false;
        var session = _sessionVersion;
        var requestId = Interlocked.Increment(ref _loadSequence);
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previousCts = Interlocked.Exchange(ref _loadCts, currentCts);
        previousCts?.Cancel();
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(uid, currentCts.Token), currentCts.Token, "加载采购详情失败");
            if (_reversalDisposed || session != _sessionVersion || requestId != Volatile.Read(ref _loadSequence) || currentCts.IsCancellationRequested) return false;
            if (!Handle(result.ToClientResult(), "加载采购详情失败")) return false;
            _loading = true;
            var dto = result.Data!; PurchaseOrderId = dto.PurchaseOrderId; OrderNo = dto.OrderNo ?? string.Empty;
            _commandSession.ObserveConfirmation(ReversalUser, uid, dto.ConfirmationStatus);
            _hadPendingWrite = HasPendingWrite;
            IsReadOnly = false;
            Version = dto.Version; ConfirmationStatus = dto.ConfirmationStatus; LifecycleStatus = dto.LifecycleStatus;
            Header.Load(dto); Lines.LoadFromDtos(dto.Lines);
            CustomerLookupText = Header.CustomerDisplay; SupplierLookupText = Header.SupplierDisplay;
            SelectedDetailTabIndex = 0; HeaderHasChanges = false;
            StatusText = HasPendingWrite ? "采购结果仍待核实，请重试原命令；当前编辑冻结。" : $"已加载 {Lines.Items.Count} 条采购行。";
            return true;
        }
        catch (OperationCanceledException) when (currentCts.IsCancellationRequested) { return false; }
        finally
        {
            _loading = false;
            if (ReferenceEquals(_loadCts, currentCts)) _loadCts = null;
            currentCts.Dispose(); if (!_reversalDisposed) NotifyState();
        }
    }

    private bool Handle<T>(ClientServiceResult<T> result, string title)
    {
        if (result.Success && result.Data is not null) return true;
        StatusText = result.Message;
        if (result.ErrorCode == SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict)
            _messages.ShowWarning("版本冲突", StatusText + " 本地修改已保留，请决定是否刷新。");
        else if (result.ErrorCode == ProcurementErrorCodes.BomBasisChanged)
        { _messages.ShowWarning("确认依据已变化", StatusText); }
        else _messages.ShowError(title, StatusText);
        return false;
    }

    private void OnHeaderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (!_loading && (IsCreateMode || CanEdit)) { HeaderHasChanges = true; NotifyState(); } }
    private void OnLinesStateChanged(object? sender, ListStateChangedEventArgs<PurchaseOrderLineEditModel> e) => NotifyState();
    private bool CanCreateCommand() => HasCreatePermission && IsCreateMode && CanInteractWithDocument && HasUnsavedChanges;
    private bool CanSaveCommand() => CanWrite && IsNotBusy && CanEdit && HasUnsavedChanges && Lines.Items.Count > 0;
    private bool CanConfirmCommand() => CanWrite && IsNotBusy && CanConfirm && !HasUnsavedChanges;
    private bool CanCancelCommand() => CanWrite && IsNotBusy && CanCancel && !HasUnsavedChanges;
    partial void OnIsCreateModeChanged(bool value) => NotifyState();
    partial void OnHeaderHasChangesChanged(bool value) => NotifyState();
    private void NotifyState()
    {
        OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(CanCancel));
        CreateCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged(); CancelConfirmationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanReorder)); ReorderLineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInteractWithDocument));
        OnPropertyChanged(nameof(HasPendingWrite)); RetryPendingCommand.NotifyCanExecuteChanged();
        VoidOrderCommand.NotifyCanExecuteChanged(); DeleteDraftLinesCommand.NotifyCanExecuteChanged(); RetryReversalCommand.NotifyCanExecuteChanged();
    }
    public void Dispose()
    {
        _reversalDisposed = true; _reversalSession.Changed -= OnReversalChanged;
        _commandSession.Changed -= OnCommandSessionChanged; _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        Header.PropertyChanged -= OnHeaderPropertyChanged;
        Lines.ListStateChanged -= OnLinesStateChanged;
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        foreach (var search in new[] { _customerSearchCts, _supplierSearchCts, _productSearchCts, _deliverySupplierSearchCts })
        { search?.Cancel(); search?.Dispose(); }
    }

    /// <summary>其他页面处理原提交后保持旧详情只读，避免旧草稿状态误导用户继续编辑。</summary>
    private void OnCommandSessionChanged(object? sender, EventArgs e)
    {
        if (_reversalDisposed) return;
        var pending = HasPendingWrite;
        if (_hadPendingWrite && !pending && IsNotBusy)
        { IsReadOnly = true; StatusText = "原采购写入已由其他页面处理，请刷新核实后继续编辑。"; }
        _hadPendingWrite = pending; NotifyState();
    }
    /// <summary>会话变化取消旧读取；原命令仍只归原账号持有。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        _sessionVersion++; _loadCts?.Cancel(); IsReadOnly = true;
        foreach (var cts in new[] { _customerSearchCts, _supplierSearchCts, _productSearchCts, _deliverySupplierSearchCts }) cts?.Cancel();
        CustomerCandidates.Clear(); SupplierCandidates.Clear(); ProductCandidates.Clear(); DeliverySupplierCandidates.Clear();
        StatusText = "会话已变化，请重新核实采购详情。"; NotifyState();
    }
}
