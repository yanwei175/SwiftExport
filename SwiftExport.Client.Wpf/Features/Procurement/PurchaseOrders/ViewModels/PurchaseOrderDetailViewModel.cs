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
    private Guid _saveCommandId, _createCommandId;
    // 未知结果冻结完整请求，重试不得重新生成指纹或版本。
    private ConfirmProcurementOrderCommand? _pendingConfirmation;
    private bool _loading;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _customerSearchCts, _supplierSearchCts, _productSearchCts, _deliverySupplierSearchCts;
    private long _loadSequence;

    public PurchaseOrderDetailViewModel(IProcurementOrderApi api, IProcurementMasterDataQueryApi masterData,
        IDialogService dialogs, IMessageService messages,
        INavigationService navigation, ICurrentUserService currentUser,
        ProcurementReversalSession? reversalSession = null, IProcurementReversalDialogService? reversalDialogs = null)
    {
        _api = api; _masterData = masterData; _dialogs = dialogs;
        _messages = messages; _navigation = navigation; _currentUser = currentUser;
        _reversalSession = reversalSession ?? new ProcurementReversalSession(); _reversalDialogs = reversalDialogs;
        _reversalSession.Changed += OnReversalChanged;
        RegisterBusyAwareCommand(VoidOrderCommand); RegisterBusyAwareCommand(DeleteDraftLinesCommand); RegisterBusyAwareCommand(RetryReversalCommand);
        Title = "采购订单详情";
        Lines.ListStateChanged += OnLinesStateChanged;
        Header.PropertyChanged += OnHeaderPropertyChanged;
        RegisterBusyAwareCommand(CreateCommand); RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(RefreshCommand); RegisterBusyAwareCommand(CloseDocumentCommand);
        RegisterBusyAwareCommand(MoveLineUpCommand); RegisterBusyAwareCommand(MoveLineDownCommand);
    }

    public PurchaseOrderHeaderEditorModel Header { get; } = new();
    public PurchaseOrderLineListModel Lines { get; } = new();
    public ObservableCollection<ProcurementCustomerCandidateDto> CustomerCandidates { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> SupplierCandidates { get; } = [];
    public ObservableCollection<ProcurementProductCandidateDto> ProductCandidates { get; } = [];
    public ObservableCollection<ProcurementSupplierCandidateDto> DeliverySupplierCandidates { get; } = [];
    public string DocumentTitle => IsCreateMode ? "新建采购" : string.IsNullOrWhiteSpace(OrderNo) ? "采购订单详情" : OrderNo;
    public bool HasUnsavedChanges => HeaderHasChanges || Lines.HasChanges;
    public bool HasReadPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);
    public bool HasCreatePermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate);
    public bool CanEdit => _pendingConfirmation is null && CanInteractWithDocument && !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit) && Header.CanEditDraft;
    public bool CanConfirm => CanInteractWithDocument && !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm) && (_pendingConfirmation is not null || Header.CanConfirm);
    public bool CanCancel => _pendingConfirmation is null && CanInteractWithDocument && !IsCreateMode
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
        => !IsBusy && (!HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭采购详情", "存在未保存修改，确定放弃吗？", "放弃并关闭", "继续编辑", cancellationToken));

    [RelayCommand]
    private void AddLine()
    {
        if (!IsCreateMode) return;
        Lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = Lines.Items.Count + 1, Quantity = 1m });
        NotifyState();
    }

    [RelayCommand]
    private void RemoveLine(PurchaseOrderLineEditModel? line)
    {
        if (!IsCreateMode || line is null || !line.IsNew) return;
        Lines.Remove(line); NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanMoveLineUp))] private void MoveLineUp(PurchaseOrderLineEditModel? line) => MoveLine(line, -1);
    [RelayCommand(CanExecute = nameof(CanMoveLineDown))] private void MoveLineDown(PurchaseOrderLineEditModel? line) => MoveLine(line, 1);

    /// <summary>仅在创建态移动新增行，并以集合顺序重新生成连续行号。</summary>
    private void MoveLine(PurchaseOrderLineEditModel? line, int offset)
    {
        if (line is null) return;
        var index = Lines.Items.IndexOf(line); var target = index + offset;
        if (index < 0 || target < 0 || target >= Lines.Items.Count || !Lines.Move(line, target)) return;
        for (var i = 0; i < Lines.Items.Count; i++) Lines.Items[i].LineNumber = i + 1;
        Lines.SelectedItem = line; NotifyState();
    }

    private bool CanMoveLineUp(PurchaseOrderLineEditModel? line)
        => IsCreateMode && IsNotBusy && line is { IsNew: true, IsDeleted: false } && Lines.Items.IndexOf(line) > 0;
    private bool CanMoveLineDown(PurchaseOrderLineEditModel? line)
    {
        var index = line is null ? -1 : Lines.Items.IndexOf(line);
        return IsCreateMode && IsNotBusy && line is { IsNew: true, IsDeleted: false } && index >= 0 && index < Lines.Items.Count - 1;
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
            if (!current.IsCancellationRequested && ReferenceEquals(_productSearchCts, current))
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
            if (!current.IsCancellationRequested && ReferenceEquals(getCurrent(), current)) ReplaceItems(target, result.Success ? result.Data?.Items : null);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally { if (ReferenceEquals(getCurrent(), current)) { setCurrent(null); setBusy(false); } current.Dispose(); }
    }

    private static CancellationTokenSource ReplaceCancellation(ref CancellationTokenSource? field)
    { field?.Cancel(); field?.Dispose(); field = new CancellationTokenSource(); return field; }
    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T>? items)
    { target.Clear(); if (items is not null) foreach (var item in items) target.Add(item); }

    [RelayCommand(CanExecute = nameof(CanCreateCommand))]
    private async Task CreateAsync()
    {
        var error = await Lines.ValidateAllAsync();
        if (SelectedCustomer is null || SelectedSupplier is null || Lines.Items.Count == 0 || error is not null)
        { _messages.ShowWarning("创建信息不完整", error ?? "请选择客户、实际供应商并至少新增一条采购行。"); return; }
        _createCommandId = _createCommandId == Guid.Empty ? Guid.NewGuid() : _createCommandId;
        var result = await _api.CreateAsync(new CreateIndependentPurchaseOrderCommand
        {
            CommandId = _createCommandId,
            CustomerId = SelectedCustomer.Uid,
            SupplierId = SelectedSupplier.Uid,
            OrderDate = OrderDate,
            Remark = Header.Remark,
            PackagingRequirements = Header.PackagingRequirements,
            Lines = Lines.Items.Select(x => new CreateIndependentPurchaseOrderLineCommand
            {
                ProductId = x.Product!.Uid,
                Quantity = x.Quantity,
                DeliverySupplierId = x.RequiresDeliverySupplier ? x.DeliverySupplierId : null,
                UnitPrice = x.UnitPrice,
                TaxPrice = x.TaxPrice,
                PlannedDeliveryDate = x.PlannedDeliveryDate,
                SupplierSku = x.SupplierSku,
                DescriptionZh = x.DescriptionZh,
                SubNote = x.SubNote
            }).ToList()
        });
        if (!Handle(result.ToClientResult(), "创建采购失败")) return;
        _createCommandId = Guid.Empty; IsCreateMode = false;
        _navigation.UpdateDocumentKey(this, $"purchase-order:new:{result.Data!.PurchaseOrderId:N}");
        await LoadAsync(result.Data.PurchaseOrderId, CancellationToken.None);
        _messages.ShowSuccess("采购已创建", result.Data.OrderNo);
    }

    [RelayCommand(CanExecute = nameof(CanSaveCommand))]
    private async Task SaveAsync()
    {
        if (!CanSaveCommand()) return;
        var error = await Lines.ValidateAllAsync();
        if (error is not null) { _messages.ShowWarning("采购行校验失败", error); return; }
        _saveCommandId = _saveCommandId == Guid.Empty ? Guid.NewGuid() : _saveCommandId;
        var result = await _api.SaveAsync(PurchaseOrderId, new UpdatePurchaseOrderDraftCommand
        {
            CommandId = _saveCommandId,
            ExpectedVersion = Version,
            Remark = Header.Remark,
            PackagingRequirements = Header.PackagingRequirements,
            DiscountRemark = Header.DiscountRemark,
            Lines = Lines.Items.Select(x => new UpdatePurchaseOrderDraftLineCommand
            {
                PurchaseOrderLineId = x.Uid,
                ExpectedVersion = x.Version,
                UnitPrice = x.UnitPrice,
                TaxPrice = x.TaxPrice,
                PlannedDeliveryDate = x.PlannedDeliveryDate,
                SupplierSku = x.SupplierSku,
                DescriptionZh = x.DescriptionZh,
                SubNote = x.SubNote
            }).ToList()
        });
        if (!Handle(result.ToClientResult(), "保存采购失败")) return;
        _saveCommandId = Guid.Empty; await LoadAsync(PurchaseOrderId, CancellationToken.None);
        _messages.ShowSuccess("采购已保存", OrderNo);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmCommand))]
    /// <summary>按原确认协议发送采购命令；明确拒绝按共享错误码处理，未知结果保留原请求。</summary>
    private async Task ConfirmAsync()
    {
        if (!CanConfirmCommand()) return;
        if (HasUnsavedChanges) { _messages.ShowWarning("请先保存", "确认前必须保存当前商务修改。"); return; }
        if (_pendingConfirmation is null)
        {
            var context = await _api.GetConfirmationContextAsync(PurchaseOrderId);
            if (!Handle(context.ToClientResult(), "读取确认依据失败")) return;
            if (!await _dialogs.ConfirmAsync("确认采购订单", $"订单 {OrderNo} 共 {Lines.Items.Count} 行，确认后锁定采购并允许入库。是否继续？", "确认")) return;
            _pendingConfirmation = new ConfirmProcurementOrderCommand
            {
                CommandId = Guid.NewGuid(), ExpectedVersion = context.Data!.Version,
                ContextFingerprint = context.Data.Fingerprint
            };
        }
        NotifyState();
        var result = await _api.ConfirmAsync(PurchaseOrderId, _pendingConfirmation);
        if (!result.Success && result.Error?.Code is ServiceErrorCodes.Validation or ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated or ProcurementErrorCodes.BomBasisChanged)
            _pendingConfirmation = null;
        NotifyState();
        if (!Handle(result.ToClientResult(), "确认采购失败")) return;
        _pendingConfirmation = null; await LoadAsync(PurchaseOrderId, CancellationToken.None);
        _messages.ShowSuccess("采购已确认", OrderNo);
    }

    [RelayCommand(CanExecute = nameof(CanCancelCommand))]
    private Task CancelConfirmationAsync() => StartReversalAsync(ProcurementReversalOperation.CancelConfirmation);

    [RelayCommand] private async Task RefreshAsync()
    {
        if (PurchaseOrderId == Guid.Empty || IsBusy) return;
        if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("刷新采购", "放弃未保存修改并重新加载？", "放弃并刷新")) return;
        await LoadAsync(PurchaseOrderId, CancellationToken.None);
    }
    [RelayCommand] private Task CloseDocumentAsync() => _navigation.CloseAsync(_navigation.OpenTabs.FirstOrDefault(x => ReferenceEquals(x.ContentViewModel, this)));

    private void InitializeCreate()
    {
        IsCreateMode = true; OrderDate = DateTime.Today; Header.LoadCreate(); Lines.Clear(); AddLine();
        HeaderHasChanges = false; StatusText = "请选择客户、实际供应商并维护采购行。"; NotifyState();
    }

    private async Task LoadAsync(Guid uid, CancellationToken ct)
    {
        var requestId = Interlocked.Increment(ref _loadSequence);
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previousCts = Interlocked.Exchange(ref _loadCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        ServiceResult<PurchaseOrderDetailDto> result;
        try
        {
            result = await _api.GetDetailAsync(uid, currentCts.Token);
        }
        catch (OperationCanceledException) when (currentCts.IsCancellationRequested)
        {
            return;
        }
        if (_reversalDisposed || requestId != Volatile.Read(ref _loadSequence) || currentCts.IsCancellationRequested) return;
        if (!Handle(result.ToClientResult(), "加载采购详情失败")) return;
        _loading = true;
        var dto = result.Data!; PurchaseOrderId = dto.PurchaseOrderId; OrderNo = dto.OrderNo ?? string.Empty;
        IsReadOnly = false;
        Version = dto.Version; ConfirmationStatus = dto.ConfirmationStatus; LifecycleStatus = dto.LifecycleStatus;
        Header.Load(dto); Lines.LoadFromDtos(dto.Lines);
        CustomerLookupText = Header.CustomerDisplay; SupplierLookupText = Header.SupplierDisplay;
        SelectedDetailTabIndex = 0; HeaderHasChanges = false; _loading = false;
        StatusText = $"已加载 {Lines.Items.Count} 条采购行。"; NotifyState();
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
    private bool CanCreateCommand() => HasCreatePermission && IsCreateMode && IsNotBusy && HasUnsavedChanges;
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
        MoveLineUpCommand.NotifyCanExecuteChanged(); MoveLineDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInteractWithDocument));
        VoidOrderCommand.NotifyCanExecuteChanged(); DeleteDraftLinesCommand.NotifyCanExecuteChanged(); RetryReversalCommand.NotifyCanExecuteChanged();
    }
    public void Dispose()
    {
        _reversalDisposed = true; _reversalSession.Changed -= OnReversalChanged;
        Header.PropertyChanged -= OnHeaderPropertyChanged;
        Lines.ListStateChanged -= OnLinesStateChanged;
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        foreach (var search in new[] { _customerSearchCts, _supplierSearchCts, _productSearchCts, _deliverySupplierSearchCts })
        { search?.Cancel(); search?.Dispose(); }
    }
}
