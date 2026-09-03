using System.ComponentModel;
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
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购详情文档；统一处理手工创建、草稿商务编辑、确认绑定及确认状态命令。</summary>
public sealed partial class PurchaseOrderDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-order-detail";
    private readonly IProcurementOrderApi _api;
    private readonly IProcurementMasterDataQueryApi _masterData;
    private readonly IWarehouseSiteApi _warehouses;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private Guid _saveCommandId, _confirmCommandId, _cancelCommandId, _createCommandId;
    private bool _loading;
    private CancellationTokenSource? _loadCts;
    private long _loadSequence;

    public PurchaseOrderDetailViewModel(IProcurementOrderApi api, IProcurementMasterDataQueryApi masterData,
        IWarehouseSiteApi warehouses, IDialogService dialogs, IMessageService messages,
        INavigationService navigation, ICurrentUserService currentUser)
    {
        _api = api; _masterData = masterData; _warehouses = warehouses; _dialogs = dialogs;
        _messages = messages; _navigation = navigation; _currentUser = currentUser;
        Title = "采购订单详情";
        Lines.ListStateChanged += OnLinesStateChanged;
        Header.PropertyChanged += OnHeaderPropertyChanged;
        RegisterBusyAwareCommand(CreateCommand); RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(RefreshCommand); RegisterBusyAwareCommand(CloseDocumentCommand);
    }

    public PurchaseOrderHeaderEditorModel Header { get; } = new();
    public PurchaseOrderLineListModel Lines { get; } = new();
    public List<ProcurementConfirmationBindingModel> ConfirmationBindings { get; } = [];
    public List<ProcurementCustomerCandidateDto> CustomerCandidates { get; } = [];
    public List<ProcurementSupplierCandidateDto> SupplierCandidates { get; } = [];
    public List<ProcurementProductCandidateDto> ProductCandidates { get; } = [];
    public List<WarehouseCandidateDto> WarehouseCandidates { get; } = [];
    public string DocumentTitle => IsCreateMode ? "新建采购" : string.IsNullOrWhiteSpace(OrderNo) ? "采购订单详情" : OrderNo;
    public bool HasUnsavedChanges => HeaderHasChanges || Lines.HasChanges;
    public bool HasReadPermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead);
    public bool HasCreatePermission => _currentUser.HasPermission(PermissionKeys.ProcurementOrderCreate);
    public bool CanEdit => !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit) && Header.CanEditDraft;
    public bool CanConfirm => !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm) && Header.CanConfirm;
    public bool CanCancel => !IsCreateMode && _currentUser.HasPermission(PermissionKeys.ProcurementOrderConfirm) && Header.CanCancelConfirmation;

    [ObservableProperty] public partial bool IsCreateMode { get; set; }
    [ObservableProperty] public partial bool HeaderHasChanges { get; set; }
    [ObservableProperty] public partial bool HasConfirmationBindings { get; set; }
    [ObservableProperty] public partial Guid PurchaseOrderId { get; set; }
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial int Version { get; set; }
    [ObservableProperty] public partial ConfirmationStatusEnum ConfirmationStatus { get; set; }
    [ObservableProperty] public partial PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
    [ObservableProperty] public partial ProcurementCustomerCandidateDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial ProcurementSupplierCandidateDto? SelectedSupplier { get; set; }
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
            await LoadCandidatesAsync(cancellationToken); InitializeCreate(); return;
        }
        if (!HasReadPermission) { StatusText = "当前账号没有读取采购订单的权限。"; return; }
        await LoadWarehouseCandidatesAsync(cancellationToken);
        await LoadAsync(request.PurchaseOrderId, cancellationToken);
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
        => !IsBusy && (!HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭采购详情", "存在未保存修改，确定放弃吗？", "放弃并关闭", "继续编辑", cancellationToken));

    [RelayCommand] private void AddLine()
    {
        if (!IsCreateMode) return;
        Lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = Lines.Items.Count + 1, Quantity = 1m });
        NotifyState();
    }

    [RelayCommand] private void RemoveLine(PurchaseOrderLineEditModel? line)
    {
        if (!IsCreateMode || line is null || !line.IsNew) return;
        Lines.Remove(line); NotifyState();
    }

    [RelayCommand(CanExecute = nameof(CanCreateCommand))]
    private async Task CreateAsync()
    {
        var error = await Lines.ValidateAllAsync();
        if (SelectedCustomer is null || SelectedSupplier is null || Lines.Items.Count == 0 || error is not null)
        { _messages.ShowWarning("创建信息不完整", error ?? "请选择客户、实际供应商并至少新增一条采购行。"); return; }
        _createCommandId = _createCommandId == Guid.Empty ? Guid.NewGuid() : _createCommandId;
        var result = await _api.CreateAsync(new CreateIndependentPurchaseOrderCommand
        {
            CommandId = _createCommandId, CustomerId = SelectedCustomer.Uid, SupplierId = SelectedSupplier.Uid,
            OrderDate = OrderDate, Remark = Header.Remark, PackagingRequirements = Header.PackagingRequirements,
            Lines = Lines.Items.Select(x => new CreateIndependentPurchaseOrderLineCommand
            {
                ProductId = x.Product!.Uid, WarehouseSiteId = x.Warehouse!.Uid, Quantity = x.Quantity,
                UnitPrice = x.UnitPrice, TaxPrice = x.TaxPrice, PlannedDeliveryDate = x.PlannedDeliveryDate,
                SupplierSku = x.SupplierSku, DescriptionZh = x.DescriptionZh, SubNote = x.SubNote
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
        var error = await Lines.ValidateAllAsync();
        if (error is not null) { _messages.ShowWarning("采购行校验失败", error); return; }
        _saveCommandId = _saveCommandId == Guid.Empty ? Guid.NewGuid() : _saveCommandId;
        var result = await _api.SaveAsync(PurchaseOrderId, new UpdatePurchaseOrderDraftCommand
        {
            CommandId = _saveCommandId, ExpectedVersion = Version, Remark = Header.Remark,
            PackagingRequirements = Header.PackagingRequirements, DiscountRemark = Header.DiscountRemark,
            Lines = Lines.Items.Select(x => new UpdatePurchaseOrderDraftLineCommand
            {
                PurchaseOrderLineId = x.Uid, ExpectedVersion = x.Version, UnitPrice = x.UnitPrice, TaxPrice = x.TaxPrice,
                PlannedDeliveryDate = x.PlannedDeliveryDate, SupplierSku = x.SupplierSku,
                DescriptionZh = x.DescriptionZh, SubNote = x.SubNote
            }).ToList()
        });
        if (!Handle(result.ToClientResult(), "保存采购失败")) return;
        _saveCommandId = Guid.Empty; await LoadAsync(PurchaseOrderId, CancellationToken.None);
        _messages.ShowSuccess("采购已保存", OrderNo);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmCommand))]
    private async Task ConfirmAsync()
    {
        if (HasUnsavedChanges) { _messages.ShowWarning("请先保存", "确认前必须保存当前商务修改。"); return; }
        var context = await _api.GetConfirmationContextAsync(PurchaseOrderId);
        if (!Handle(context.ToClientResult(), "读取确认依据失败")) return;
        ConfirmationBindings.Clear();
        foreach (var line in context.Data!.Lines)
            foreach (var node in line.Bom.Nodes.Where(x => x.IsCustomerSupplied))
                ConfirmationBindings.Add(new ProcurementConfirmationBindingModel { PurchaseOrderLineId = line.PurchaseOrderLineId, Path = node.Path, ComponentSku = node.ComponentSkuSnapshot });
        HasConfirmationBindings = ConfirmationBindings.Count > 0; OnPropertyChanged(nameof(ConfirmationBindings));
        if (ConfirmationBindings.Any(x => x.Warehouse is null))
        { SelectedDetailTabIndex = 1; _messages.ShowWarning("需要消耗仓绑定", "请为每条客供件 BOM 路径选择消耗仓库，然后再次确认。"); return; }
        if (!await _dialogs.ConfirmAsync("确认采购订单", $"订单 {OrderNo} 共 {Lines.Items.Count} 行，将冻结当前 BOM/消耗快照。是否继续？", "确认")) return;
        _confirmCommandId = _confirmCommandId == Guid.Empty ? Guid.NewGuid() : _confirmCommandId;
        var result = await _api.ConfirmAsync(PurchaseOrderId, new ConfirmProcurementOrderCommand
        {
            CommandId = _confirmCommandId, ExpectedVersion = context.Data.Version, ContextFingerprint = context.Data.Fingerprint,
            ConsumptionBindings = ConfirmationBindings.Select(x => new ProcurementConsumptionBinding
            { PurchaseOrderLineId = x.PurchaseOrderLineId, Path = x.Path, WarehouseSiteId = x.Warehouse!.Uid }).ToList()
        });
        if (!Handle(result.ToClientResult(), "确认采购失败")) return;
        _confirmCommandId = Guid.Empty; await LoadAsync(PurchaseOrderId, CancellationToken.None);
        _messages.ShowSuccess("采购已确认", OrderNo);
    }

    [RelayCommand(CanExecute = nameof(CanCancelCommand))]
    private async Task CancelConfirmationAsync()
    {
        if (string.IsNullOrWhiteSpace(CancellationReason)) { _messages.ShowWarning("需要原因", "取消确认原因必填。"); return; }
        if (!await _dialogs.ConfirmAsync("取消采购确认", "服务端将检查入库等下游事实。", "取消确认")) return;
        _cancelCommandId = _cancelCommandId == Guid.Empty ? Guid.NewGuid() : _cancelCommandId;
        var result = await _api.CancelConfirmationAsync(PurchaseOrderId, new CancelProcurementOrderConfirmationCommand
        { CommandId = _cancelCommandId, ExpectedVersion = Version, Reason = CancellationReason.Trim() });
        if (!Handle(result.ToClientResult(), "取消确认失败")) return;
        _cancelCommandId = Guid.Empty; CancellationReason = string.Empty;
        await LoadAsync(PurchaseOrderId, CancellationToken.None); _messages.ShowSuccess("已取消确认", OrderNo);
    }

    [RelayCommand] private Task RefreshAsync() => PurchaseOrderId == Guid.Empty ? Task.CompletedTask : LoadAsync(PurchaseOrderId, CancellationToken.None);
    [RelayCommand] private Task CloseDocumentAsync() => _navigation.CloseAsync(_navigation.OpenTabs.FirstOrDefault(x => ReferenceEquals(x.ContentViewModel, this)));

    private void InitializeCreate()
    {
        IsCreateMode = true; OrderDate = DateTime.Today; Header.LoadCreate(); Lines.Clear(); AddLine();
        HeaderHasChanges = false; StatusText = "请选择客户、实际供应商并维护采购行。"; NotifyState();
    }

    private async Task LoadCandidatesAsync(CancellationToken ct)
    {
        await LoadWarehouseCandidatesAsync(ct);
        var page = new PagedRequestBase { PageIndex = 1, PageSize = 100 };
        var customers = await _masterData.SearchCustomersAsync(page, ct);
        var suppliers = await _masterData.SearchSuppliersAsync(page, ct);
        var products = await _masterData.SearchProductsAsync(page, ct);
        if (customers.Success && customers.Data is not null) CustomerCandidates.AddRange(customers.Data.Items);
        if (suppliers.Success && suppliers.Data is not null) SupplierCandidates.AddRange(suppliers.Data.Items);
        if (products.Success && products.Data is not null) ProductCandidates.AddRange(products.Data.Items);
    }

    private async Task LoadWarehouseCandidatesAsync(CancellationToken ct)
    {
        WarehouseCandidates.Clear();
        var result = await _warehouses.SearchCandidatesAsync(new PagedRequestBase { PageIndex = 1, PageSize = 100 }, ct);
        if (result.Success && result.Data is not null) WarehouseCandidates.AddRange(result.Data.Items);
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
        if (requestId != Volatile.Read(ref _loadSequence) || currentCts.IsCancellationRequested) return;
        if (!Handle(result.ToClientResult(), "加载采购详情失败")) return;
        _loading = true;
        var dto = result.Data!; PurchaseOrderId = dto.PurchaseOrderId; OrderNo = dto.OrderNo ?? string.Empty;
        Version = dto.Version; ConfirmationStatus = dto.ConfirmationStatus; LifecycleStatus = dto.LifecycleStatus;
        Header.LoadNew(dto); Lines.LoadFromDtos(dto.Lines); ConfirmationBindings.Clear(); HasConfirmationBindings = false;
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
        { ConfirmationBindings.Clear(); HasConfirmationBindings = false; _messages.ShowWarning("确认依据已变化", StatusText); }
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
    }
    public void Dispose()
    {
        Header.PropertyChanged -= OnHeaderPropertyChanged;
        Lines.ListStateChanged -= OnLinesStateChanged;
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
