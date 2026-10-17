using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>已有物料的资料、BOM 需求、独立标签和包装详情文档；BOM 绑定和组成仍由管理页维护。</summary>
public sealed partial class ProductDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>物料详情稳定路由键。</summary>
    public const string RouteKey = "product-detail";
    private readonly IProductApi _api;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService? _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService? _currentUser;
    private readonly IProductMasterApi? _productMasterApi;
    private readonly IProductNeutralManagerApi? _productNeutralApi;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _loadCts, _supplierCts, _manufacturerCts, _masterLookupCts, _neutralLookupCts;
    private Guid? _resolvedMasterId, _resolvedProfileId;
    private string _resolvedProfileName = string.Empty, _resolvedCategoryName = string.Empty;
    private long _loadSequence, _sessionSequence;
    private bool _initialized, _disposed;

    /// <summary>初始化已有物料详情及对称释放的模型、缓存、身份订阅。</summary>
    public ProductDetailViewModel(IProductApi api, ISupplierApi supplierApi, IApiCallRunner runner,
        IReferenceDataCacheService cache, IMessageService messages, IDialogService dialogs,
        INavigationService navigation, ICurrentUserService? currentUser = null,
        IProductMasterApi? productMasterApi = null, IProductNeutralManagerApi? productNeutralApi = null)
    {
        _api = api; _supplierApi = supplierApi; _runner = runner; _cache = cache;
        _messages = messages; _dialogs = dialogs; _navigation = navigation; _currentUser = currentUser;
        _productMasterApi = productMasterApi; _productNeutralApi = productNeutralApi;
        Title = "物料详情";
        Packaging = new ProductPackagingEditorViewModel(api, runner, cache);
        Packaging.PropertyChanged += OnPackagingChanged;
        Header.PropertyChanged += OnHeaderChanged;
        Tags.StateChanged += OnTagsChanged;
        PropertyChanged += OnDocumentChanged;
        if (_cache is not null) _cache.ProductAttributesChanged += OnAttributesChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        foreach (var command in new IRelayCommand[] { SaveCommand, RefreshCommand, RejectDetailChangesCommand,
            RefreshProductAttributesCommand, CommitColorCommand, ClearColorCommand, CommitSupplierCommand,
            ClearSupplierCommand, CommitManufacturerCommand, ClearManufacturerCommand, CommitWarehouseCommand, ClearWarehouseCommand,
            CommitUnitCommand, ClearUnitCommand,
            CommitProductMasterCommand, ClearProductMasterCommand, CommitProductNeutralCommand, ClearProductNeutralCommand,
            SearchSuppliersCommand, SearchManufacturersCommand })
            RegisterBusyAwareCommand(command);
        ApplyPermission();
    }

    /// <summary>本页可写资料草稿。</summary>
    public ProductEditModel Header { get; } = new();
    /// <summary>本物料独立选值，不复制主型号选值。</summary>
    public ProductAttributeSelectionModel Tags { get; } = new();
    /// <summary>包装子区块拥有自己的行草稿，由顶部统一保存。</summary>
    public ProductPackagingEditorViewModel Packaging { get; }
    /// <summary>已读取的只读来源、包装快照；标签刷新不修改字段草稿或预期版本。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceStatus))]
    [NotifyPropertyChangedFor(nameof(TemplateStatus))]
    public partial ProductDetailDto? Source { get; private set; }
    /// <summary>颜色、单位等基础资料缓存。</summary>
    public IReferenceDataCacheService? ReferenceDataCache => _cache;
    /// <summary>默认供应商独立搜索结果。</summary>
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    /// <summary>制造商独立搜索结果。</summary>
    public ObservableCollection<SupplierDto> ManufacturerCandidates { get; } = [];
    /// <summary>供物料独立选择的主型号候选。</summary>
    public ObservableCollection<ProductMasterLookupItemModel> ProductMasterCandidates { get; } = [];
    /// <summary>物料型号的中性产品编码建议，不按主型号过滤或建立关系。</summary>
    public ObservableCollection<ProductNeutralLookupItemModel> ProductNeutralCandidates { get; } = [];
    /// <summary>产品专用权限下完整发布的参考仓库候选。</summary>
    public ObservableCollection<WarehouseSitePickerDto> WarehouseCandidates { get; } = [];
    /// <summary>单位字典候选，保存英文代码。</summary>
    public IReadOnlyList<SysDictDataDto> UnitCandidates => _cache?.GetDictionaryValues(DictCodes.Unit) ?? [];
    /// <summary>沿用物料类型候选。</summary>
    public IReadOnlyList<string> MaterialTypes { get; } = MaterialTypesEnum.MaterialTypes;
    /// <summary>沿用供货类型候选。</summary>
    public IReadOnlyList<string> SupplyTypes { get; } = SupplyTypeEnums.SupplyTypes;
    /// <summary>物料详情标题。</summary>
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.Sku) ? "物料详情" : $"物料 · {Header.Sku}";
    /// <summary>资料字段、已确认标签或包装行是否存在草稿。</summary>
    public bool HasUnsavedChanges => Header.IsDirty || Tags.IsDirty || Packaging.HasChanges;
    /// <summary>写权限与 Busy 共同控制基础字段。</summary>
    public bool CanEditHeader => !_disposed && Header.Uid != Guid.Empty && CanWrite && IsNotBusy;
    /// <summary>有效模板与完整定义共同控制标签值编辑。</summary>
    public bool CanEditTags => CanEditHeader && Tags.CanEdit;
    /// <summary>只读启停状态。</summary>
    public string SourceStatus => Source is null ? "未加载" : Source.IsActive ? "启用" : "停用";
    /// <summary>只读模板名称或无法配置标签的来源说明。</summary>
    public string TemplateStatus => (Header.ProductMasterId ?? Guid.Empty) == Guid.Empty
        ? "未关联主型号，暂不能配置标签。"
        : _resolvedMasterId != Header.ProductMasterId || string.IsNullOrWhiteSpace(Header.MasterSku)
            ? "主型号引用已失效或尚未读取，已有标签保持。"
            : string.IsNullOrWhiteSpace(_resolvedProfileName) ? "来源主型号尚未设置属性模板。" : _resolvedProfileName;
    /// <summary>当前草稿主型号提供的只读分类名称。</summary>
    public string SourceCategoryName => _resolvedMasterId == Header.ProductMasterId ? _resolvedCategoryName : string.Empty;
    /// <summary>视图采集尚未提交的原生输入；无效数值阻止保存但保留输入。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>放弃后恢复原生绑定目标，覆盖无效输入缓冲。</summary>
    public Action? RefreshEditorTargets { get; set; }
    /// <summary>当前文档加载或保存反馈。</summary>
    [ObservableProperty] public partial string StatusText { get; private set; } = "正在准备物料详情。";
    /// <summary>待确认颜色候选。</summary>
    [ObservableProperty] public partial ProductColorDto? SelectedColor { get; set; }
    /// <summary>待确认默认供应商候选。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    /// <summary>待确认制造商候选。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedManufacturer { get; set; }
    /// <summary>待确认参考仓库候选。</summary>
    [ObservableProperty] public partial WarehouseSitePickerDto? SelectedWarehouse { get; set; }
    /// <summary>单位本地候选，只在明确确认后写入单位代码，候选刷新不能清空旧单位。</summary>
    [ObservableProperty] public partial SysDictDataDto? SelectedUnit { get; set; }
    /// <summary>默认供应商查询状态。</summary>
    [ObservableProperty] public partial bool IsSupplierLookupBusy { get; private set; }
    /// <summary>制造商查询状态。</summary>
    [ObservableProperty] public partial bool IsManufacturerLookupBusy { get; private set; }
    /// <summary>待确认主型号候选，候选取消不会改动引用。</summary>
    [ObservableProperty] public partial ProductMasterLookupItemModel? SelectedProductMaster { get; set; }
    /// <summary>待确认型号编码建议，选择不回写主型号或颜色。</summary>
    [ObservableProperty] public partial ProductNeutralLookupItemModel? SelectedProductNeutral { get; set; }
    /// <summary>主型号候选加载状态。</summary>
    [ObservableProperty] public partial bool IsProductMasterLookupBusy { get; private set; }
    /// <summary>中性产品候选加载状态。</summary>
    [ObservableProperty] public partial bool IsProductNeutralLookupBusy { get; private set; }

    /// <summary>首次按身份加载已有物料；重复打开或激活不覆盖详情草稿。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_disposed || _initialized) return;
        if (parameter is not ProductDocumentOpenRequest request || request.ProductId == Guid.Empty)
        { StatusText = "请从物料管理页选择已保存物料后打开详情。"; return; }
        _initialized = true;
        var session = Volatile.Read(ref _sessionSequence);
        if (_cache is not null)
        {
            var result = await _cache.EnsureLoadedAsync();
            if (!IsAlive(session) || cancellationToken.IsCancellationRequested) return;
            if (!result.Success) _messages.ShowWarning("基础资料未完整加载", result.Message);
            OnPropertyChanged(nameof(UnitCandidates));
        }
        await LoadAsync(request.ProductId, cancellationToken);
    }

    /// <summary>关闭前采集原生字段；写入和标签弹出层未完成时不能关闭。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || Tags.IsPopupOpen) return false;
        var valid = CommitPendingEdits?.Invoke() ?? true;
        return valid && !HasUnsavedChanges || await _dialogs.ConfirmAsync("放弃未保存变更",
            "物料详情有未保存修改，确定关闭吗？", "关闭", cancellationToken: cancellationToken);
    }

    /// <summary>资料和标签同请求提交；失败、版本冲突、旧会话响应均不接纳编辑基线。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !(CommitPendingEdits?.Invoke() ?? true)) return;
        if (!HasUnsavedChanges) { StatusText = "没有需要保存的修改。"; return; }
        await RunWriteAsync(async () =>
        {
            // 没有明确修改标签时采用 null 保持协议；切换或丢失来源不能隐式清空旧标签。
            var selectedIds = Tags.IsDirty ? Tags.GetSelectedOptionIds() : null;
            var error = Header.ValidateRow() ?? (Tags.IsDirty ? Tags.ValidateSelection(_resolvedProfileId ?? Guid.Empty) : null) ?? Packaging.Validate();
            if (error is not null) { ValidationSummary = error; _messages.ShowWarning("物料未保存", error); return; }
            if (!CanWrite || _disposed) return;
            var session = Volatile.Read(ref _sessionSequence);
            var request = new SaveProductDetailRequest { ProductId = Header.Uid, ExpectedVersion = Header.Version,
                Data = Header.ToWriteDto(), SelectedOptionIds = selectedIds, PackagingChanges = Packaging.CreateChangesRequest() };
            var result = await _runner.RunAsync(() => _api.SaveDetailAsync(Header.Uid, request, _lifetimeCts.Token),
                _lifetimeCts.Token, "保存物料详情失败");
            if (!IsAlive(session)) return;
            if (!result.Success || result.Data?.Detail is null)
            {
                ValidationSummary = result.Message;
                if (result.Error?.Code == ServiceErrorCodes.VersionConflict)
                    _messages.ShowWarning("版本冲突", "物料已被其他用户修改，本地编辑已保留。请决定是否刷新。");
                else _messages.ShowError("保存物料详情失败", result.Message);
                return;
            }
            AcceptDetail(result.Data.Detail);
            WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(Header.Uid));
            _messages.ShowSuccess("物料已保存", Header.Sku);
        }, "正在保存物料详情...");
    }

    /// <summary>整页刷新先保护真实草稿及无效输入，读取失败保留编辑。</summary>
    [RelayCommand(CanExecute = nameof(CanReadCommand))]
    private async Task RefreshAsync()
    {
        if (!CanReadCommand() || Header.Uid == Guid.Empty || Tags.IsPopupOpen) return;
        var valid = CommitPendingEdits?.Invoke() ?? true;
        if ((!valid || HasUnsavedChanges) && !await _dialogs.ConfirmAsync("放弃未保存变更",
            "刷新将放弃当前物料详情修改，是否继续？", "刷新")) return;
        if (CanReadCommand()) await LoadAsync(Header.Uid, _lifetimeCts.Token);
    }

    /// <summary>撤销字段与独立选值，保留原预期版本及最新只读模板来源。</summary>
    [RelayCommand(CanExecute = nameof(CanReadCommand))]
    private void RejectDetailChanges()
    {
        if (!CanReadCommand() || Tags.IsPopupOpen) return;
        Header.RejectChanges(); Tags.RejectChanges(); Packaging.RejectChanges();
        if (_resolvedMasterId != Header.ProductMasterId && Source is { } source && source.Data.ProductMasterId == Header.ProductMasterId)
            SetResolvedMaster(Header.ProductMasterId, source.ProductAttributeProfileId, source.ProductAttributeProfileName, source.CategoryName);
        else RefreshTagCandidates();
        SyncLookupSelections(); RefreshEditorTargets?.Invoke();
        ValidationSummary = null; NotifyDocumentState();
    }

    /// <summary>通过工作区统一关闭门禁退出详情。</summary>
    [RelayCommand]
    private Task CloseDocumentAsync() => _navigation.CloseAsync(
        _navigation.OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.ContentViewModel, this)));

    /// <summary>刷新完整定义与最新模板来源，原物料版本、字段和选值草稿保持，不自动删失效值。</summary>
    [RelayCommand(CanExecute = nameof(CanReadCommand))]
    private async Task RefreshProductAttributesAsync()
    {
        if (!CanReadCommand() || _cache is null || Header.Uid == Guid.Empty) return;
        await RunBusyAsync(async () =>
        {
            var session = Volatile.Read(ref _sessionSequence);
            var masterId = Header.ProductMasterId;
            Guid? profileId = null;
            string? profileName = null, categoryName = null;
            ProductDetailDto? refreshedSource = null;
            if (masterId.HasValue && _productMasterApi is not null)
            {
                var master = await _runner.RunAsync(() => _productMasterApi.GetDetailAsync(masterId.Value, _lifetimeCts.Token),
                    _lifetimeCts.Token, "读取主型号模板来源失败");
                if (!IsAlive(session)) return;
                if (!master.Success || master.Data is null) { _messages.ShowWarning("模板来源未刷新", master.Message); return; }
                profileId = master.Data.Master.ProductAttributeProfileId;
                profileName = master.Data.ProductAttributeProfileName;
                categoryName = master.Data.CategoryName;
            }
            else if (masterId.HasValue)
            {
                var source = await _runner.RunAsync(() => _api.GetDetailAsync(Header.Uid, _lifetimeCts.Token),
                    _lifetimeCts.Token, "读取物料模板来源失败");
                if (!IsAlive(session)) return;
                if (!source.Success || source.Data is null || source.Data.Data.ProductMasterId != masterId)
                { _messages.ShowWarning("模板来源未刷新", source.Message); return; }
                profileId = source.Data.ProductAttributeProfileId; profileName = source.Data.ProductAttributeProfileName;
                categoryName = source.Data.CategoryName;
                refreshedSource = source.Data;
            }
            var refreshed = await _cache.RefreshProductAttributesAsync();
            if (!IsAlive(session)) return;
            if (!refreshed.Success) { _messages.ShowWarning("标签未刷新", refreshed.Message); return; }
            if (Header.ProductMasterId != masterId) return;
            if (refreshedSource is not null) Source = refreshedSource;
            SetResolvedMaster(masterId, profileId, profileName, categoryName);
            OnPropertyChanged(nameof(UnitCandidates)); SyncSelectedUnit();
        }, "正在刷新标签及来源模板...");
    }

    /// <summary>默认供应商搜索由新查询替代旧查询，不能回写释放后的页面。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup), AllowConcurrentExecutions = true)]
    private Task SearchSuppliersAsync(string? keyword) => SearchSupplierCoreAsync(keyword, false);
    /// <summary>制造商搜索具有独立请求身份及候选集合。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup), AllowConcurrentExecutions = true)]
    private Task SearchManufacturersAsync(string? keyword) => SearchSupplierCoreAsync(keyword, true);

    /// <summary>仅明确确认供应商时写入身份与名称。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitSupplier()
    { if (!CanEditLookup() || SelectedSupplier is null) return; Header.DefaultSupplierId = SelectedSupplier.Uid; Header.DefaultSupplierCode = SelectedSupplier.SupplierCode ?? string.Empty; Header.SupplierName = SelectedSupplier.SupplierName ?? string.Empty; }
    /// <summary>明确清空可选供应商，不影响制造商。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearSupplier()
    { if (!CanEditLookup()) return; Header.DefaultSupplierId = null; Header.DefaultSupplierCode = Header.SupplierName = string.Empty; SelectedSupplier = null; }
    /// <summary>确认独立制造商，不改变默认供应商。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitManufacturer()
    { if (!CanEditLookup() || SelectedManufacturer is null) return; Header.ManufacturerId = SelectedManufacturer.Uid; Header.ManufacturerCode = SelectedManufacturer.SupplierCode ?? string.Empty; Header.ManufacturerName = SelectedManufacturer.SupplierName ?? string.Empty; }
    /// <summary>清除制造商身份但保留独立制造商型号。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearManufacturer()
    { if (!CanEditLookup()) return; Header.ManufacturerId = null; Header.ManufacturerCode = Header.ManufacturerName = string.Empty; SelectedManufacturer = null; }
    /// <summary>确认物料参考仓库，不替代单据实际仓库。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitWarehouse()
    { if (!CanEditLookup() || SelectedWarehouse is null) return; Header.DefaultWarehouseSiteId = SelectedWarehouse.Uid; Header.DefaultWarehouseCode = SelectedWarehouse.Code ?? string.Empty; Header.DefaultWarehouseName = SelectedWarehouse.Name ?? string.Empty; }
    /// <summary>明确清除可选参考仓库。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearWarehouse()
    { if (!CanEditLookup()) return; Header.DefaultWarehouseSiteId = null; Header.DefaultWarehouseCode = Header.DefaultWarehouseName = string.Empty; SelectedWarehouse = null; }
    /// <summary>确认颜色身份并更新显示名。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitColor()
    { if (!CanEditLookup() || SelectedColor is null) return; Header.ColorId = SelectedColor.Uid; Header.Finish = SelectedColor.ColorNameZh ?? SelectedColor.ColorNameEn ?? string.Empty; }
    /// <summary>明确清除颜色身份。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearColor()
    { if (!CanEditLookup()) return; Header.ColorId = null; Header.Finish = string.Empty; SelectedColor = null; }
    /// <summary>明确确认有效单位候选，仅保存英文代码。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitUnit()
    { if (!CanEditLookup() || SelectedUnit is null) return; Header.Unit = SelectedUnit.DataNameEn ?? string.Empty; }
    /// <summary>明确清除可选单位，关闭或搜索失配不触发清除。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearUnit()
    { if (!CanEditLookup()) return; Header.Unit = string.Empty; SelectedUnit = null; }

    /// <summary>搜索主型号取消旧请求；不使用中性产品推导或限制来源。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup), AllowConcurrentExecutions = true)]
    private async Task SearchProductMastersAsync(string? keyword)
    {
        if (!CanEditLookup() || _productMasterApi is null) return;
        var current = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _masterLookupCts, current); previous?.Cancel(); previous?.Dispose();
        var session = Volatile.Read(ref _sessionSequence); IsProductMasterLookupBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _productMasterApi.GetPagedAsync(
                new ProductMasterSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 50 }, current.Token), current.Token, "搜索主型号失败");
            if (!IsAlive(session) || current.IsCancellationRequested || !ReferenceEquals(current, _masterLookupCts)) return;
            if (!result.Success) { _messages.ShowWarning("主型号候选未加载", result.Message); return; }
            ProductMasterCandidates.Clear(); foreach (var item in result.Data?.Items ?? []) ProductMasterCandidates.Add(new(item));
        }
        finally { if (!_disposed && ReferenceEquals(current, _masterLookupCts)) IsProductMasterLookupBusy = false; }
    }
    /// <summary>中性产品独立查询，旧查询或释放后的结果不能回写。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup), AllowConcurrentExecutions = true)]
    private async Task SearchProductNeutralsAsync(string? keyword)
    {
        if (!CanEditLookup() || _productNeutralApi is null) return;
        var current = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _neutralLookupCts, current); previous?.Cancel(); previous?.Dispose();
        var session = Volatile.Read(ref _sessionSequence); IsProductNeutralLookupBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _productNeutralApi.GetPagedAsync(
                new ProductNeutralManagerSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 50 }, current.Token), current.Token, "搜索中性产品失败");
            if (!IsAlive(session) || current.IsCancellationRequested || !ReferenceEquals(current, _neutralLookupCts)) return;
            if (!result.Success) { _messages.ShowWarning("中性产品候选未加载", result.Message); return; }
            ProductNeutralCandidates.Clear(); foreach (var item in result.Data?.Items ?? []) ProductNeutralCandidates.Add(new(item));
        }
        finally { if (!_disposed && ReferenceEquals(current, _neutralLookupCts)) IsProductNeutralLookupBusy = false; }
    }
    /// <summary>确认主型号只改变该引用及其模板显示；保留物料型号、颜色和已有标签身份。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitProductMaster()
    {
        if (!CanEditLookup() || SelectedProductMaster is not { } master || Tags.IsPopupOpen) return;
        Header.ProductMasterId = master.Uid; Header.MasterSku = master.MasterSku ?? string.Empty;
        SetResolvedMaster(master.Uid, master.ProductAttributeProfileId, master.ProductAttributeProfileName, master.CategoryName);
    }
    /// <summary>明确清空主型号为 null，不清空物料型号或标签选择。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearProductMaster()
    {
        if (!CanEditLookup() || Tags.IsPopupOpen) return;
        Header.ProductMasterId = null; Header.MasterSku = string.Empty; SelectedProductMaster = null;
        SetResolvedMaster(null, null, null, null);
    }
    /// <summary>确认候选仅填写物料自身型号，不改变主型号、模板或颜色。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitProductNeutral()
    {
        if (!CanEditLookup() || SelectedProductNeutral is not { } neutral) return;
        Header.LinkSku = neutral.ProductCode ?? string.Empty;
    }
    /// <summary>明确清空物料自身型号，主型号和其他独立资料保持。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearProductNeutral()
    {
        if (!CanEditLookup()) return;
        Header.LinkSku = string.Empty; SelectedProductNeutral = null;
    }

    /// <summary>详情读取具有取消、请求序号及会话存活门禁，整次加载只持有一层 Busy。</summary>
    private async Task LoadAsync(Guid id, CancellationToken externalToken)
    {
        var sequence = Interlocked.Increment(ref _loadSequence);
        var current = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _lifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _loadCts, current); previous?.Cancel(); previous?.Dispose();
        var session = Volatile.Read(ref _sessionSequence);
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(id, current.Token), current.Token, "加载物料详情失败");
            if (!IsAlive(session) || current.IsCancellationRequested || sequence != Volatile.Read(ref _loadSequence)) return;
            if (!result.Success || result.Data is null)
            { StatusText = result.Message; if (!string.IsNullOrEmpty(result.Message)) _messages.ShowError("加载物料详情失败", result.Message); return; }
            AcceptDetail(result.Data); RefreshEditorTargets?.Invoke();
            await LoadWarehouseCandidatesAsync(current.Token, session);
        }, "正在加载物料详情...");
    }

    /// <summary>成功读快照成为字段与标签基线，选择器程序回填不产生草稿。</summary>
    private void AcceptDetail(ProductDetailDto detail)
    {
        Source = detail; Header.Load(detail);
        _resolvedMasterId = Header.ProductMasterId;
        _resolvedProfileId = detail.ProductAttributeProfileId;
        _resolvedProfileName = detail.ProductAttributeProfileName ?? string.Empty;
        _resolvedCategoryName = detail.CategoryName ?? string.Empty;
        Tags.Load(detail.ProductAttributeProfileId ?? Guid.Empty, detail.SelectedOptionIds, detail.AttributeSelections);
        Packaging.Load(detail.Uid, detail.Packages);
        RefreshTagCandidates(); SyncLookupSelections();
        StatusText = $"已加载物料，当前版本 {Header.Version}。"; AcceptChanges(); NotifyDocumentState();
    }

    /// <summary>产品专用候选逐页读取，全部成功后发布，失败保留旧完整集合。</summary>
    private async Task LoadWarehouseCandidatesAsync(CancellationToken token, long session)
    {
        var loaded = new List<WarehouseSitePickerDto>();
        for (var page = 1; ; page++)
        {
            var result = await _runner.RunAsync(() => _api.GetDefaultWarehouseCandidatesAsync(
                new PagedRequestBase { PageIndex = page, PageSize = 200 }, token), token, "加载参考仓库失败");
            if (!IsAlive(session) || token.IsCancellationRequested) return;
            if (!result.Success || result.Data is null) { _messages.ShowWarning("参考仓库候选未加载", result.Message); return; }
            loaded.AddRange(result.Data.Items.Select(item => new WarehouseSitePickerDto { Uid = item.Uid, Code = item.Code, Name = item.Name, IsActive = true }));
            if (loaded.Count >= result.Data.TotalCount || result.Data.Items.Count == 0) break;
        }
        WarehouseCandidates.Clear(); foreach (var item in loaded) WarehouseCandidates.Add(item);
        SyncLookupSelections();
    }

    /// <summary>供应商旧接口无 HTTP 取消参数；取消等待并用请求身份拒绝过期回写，失败保留候选。</summary>
    private async Task SearchSupplierCoreAsync(string? keyword, bool manufacturer)
    {
        if (!CanEditLookup()) return;
        var current = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var previous = manufacturer ? Interlocked.Exchange(ref _manufacturerCts, current) : Interlocked.Exchange(ref _supplierCts, current);
        previous?.Cancel(); previous?.Dispose();
        if (manufacturer) IsManufacturerLookupBusy = true; else IsSupplierLookupBusy = true;
        var session = Volatile.Read(ref _sessionSequence);
        try
        {
            var result = await _runner.RunAsync(() => _supplierApi.GetPagedAsync(
                new SupplierSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 30 }).WaitAsync(current.Token),
                current.Token, "搜索供应商失败");
            if (!IsAlive(session) || current.IsCancellationRequested || !ReferenceEquals(current, manufacturer ? _manufacturerCts : _supplierCts)) return;
            if (!result.Success) { if (!string.IsNullOrEmpty(result.Message)) _messages.ShowWarning("供应商搜索失败", result.Message); return; }
            var candidates = manufacturer ? ManufacturerCandidates : SupplierCandidates;
            candidates.Clear(); foreach (var item in result.Data?.Items ?? []) candidates.Add(item);
        }
        finally
        { if (!_disposed && ReferenceEquals(current, manufacturer ? _manufacturerCts : _supplierCts)) { if (manufacturer) IsManufacturerLookupBusy = false; else IsSupplierLookupBusy = false; } }
    }

    /// <summary>加载、放弃或候选刷新后恢复确认选择显示，不执行提交。</summary>
    private void SyncLookupSelections()
    {
        SyncSelectedUnit();
        SelectedProductMaster = Header.ProductMasterId is { } masterId
            ? ProductMasterCandidates.FirstOrDefault(item => item.Uid == masterId)
                ?? new ProductMasterLookupItemModel(new ProductMasterListItemDto { Uid = masterId, Data = new() { MasterSku = Header.MasterSku } }) : null;
        // 型号只是自身文本；没有候选也正常显示，不生成中性产品的伪身份。
        SelectedProductNeutral = ProductNeutralCandidates.FirstOrDefault(item =>
            string.Equals(item.ProductCode, Header.LinkSku, StringComparison.OrdinalIgnoreCase));
        SelectedColor = Header.ColorId is { } color && color != Guid.Empty
            ? _cache?.ProductColors.FirstOrDefault(item => item.Uid == color) ?? new ProductColorDto { Uid = color, ColorNameZh = Header.Finish } : null;
        SelectedSupplier = Header.DefaultSupplierId is { } supplier && supplier != Guid.Empty
            ? new SupplierDto { Uid = supplier, SupplierCode = Header.DefaultSupplierCode, SupplierName = Header.SupplierName } : null;
        SelectedManufacturer = Header.ManufacturerId is { } manufacturer && manufacturer != Guid.Empty
            ? new SupplierDto { Uid = manufacturer, SupplierCode = Header.ManufacturerCode, SupplierName = Header.ManufacturerName } : null;
        SelectedWarehouse = Header.DefaultWarehouseSiteId is { } warehouse && warehouse != Guid.Empty
            ? WarehouseCandidates.FirstOrDefault(item => item.Uid == warehouse) ?? new WarehouseSitePickerDto { Uid = warehouse, Code = Header.DefaultWarehouseCode, Name = Header.DefaultWarehouseName } : null;
    }

    /// <summary>按代码同步单位显示；未知或停用的旧代码保留，只读候选替换不修改字段或其基线。</summary>
    private void SyncSelectedUnit() => SelectedUnit = string.IsNullOrWhiteSpace(Header.Unit) ? null
        : UnitCandidates.FirstOrDefault(item => string.Equals(item.DataNameEn, Header.Unit, StringComparison.OrdinalIgnoreCase))
          ?? new SysDictDataDto { DictCode = DictCodes.Unit, DataNameEn = Header.Unit, DataNameZh = Header.Unit, IsEnabled = false };

    /// <summary>刷新只读模板来源，不当作用户切换模板，不清除选择或覆盖选值基线。</summary>
    private void RefreshTagCandidates()
    {
        var id = _resolvedMasterId == Header.ProductMasterId ? _resolvedProfileId ?? Guid.Empty : Guid.Empty;
        var resolution = id == Guid.Empty ? new ProductAttributeResolution(true, TemplateStatus, [], [])
            : _cache?.ResolveProductAttributes(id) ?? new(false, "标签定义尚未加载，请刷新标签。", [], []);
        Tags.RefreshSourceProfile(id, resolution); NotifyDocumentState();
    }
    /// <summary>只刷新当前草稿主型号提供的模板与分类；标签工作区保留原选择及回滚基线。</summary>
    private void SetResolvedMaster(Guid? masterId, Guid? profileId, string? profileName, string? categoryName)
    {
        _resolvedMasterId = masterId; _resolvedProfileId = profileId;
        _resolvedProfileName = profileName ?? string.Empty; _resolvedCategoryName = categoryName ?? string.Empty;
        OnPropertyChanged(nameof(TemplateStatus)); OnPropertyChanged(nameof(SourceCategoryName)); RefreshTagCandidates();
    }
    private void OnAttributesChanged(object? sender, EventArgs e) { if (!_disposed) { OnPropertyChanged(nameof(UnitCandidates)); SyncSelectedUnit(); RefreshTagCandidates(); } }
    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductEditModel.ProductMasterId))
        {
            OnPropertyChanged(nameof(TemplateStatus)); OnPropertyChanged(nameof(SourceCategoryName));
            RefreshTagCandidates();
        }
        NotifyDocumentState();
    }
    private void OnTagsChanged(object? sender, EventArgs e) => NotifyDocumentState();
    /// <summary>包装脏状态加入同一文档的关闭、刷新和保存保护，不镜像行草稿。</summary>
    private void OnPackagingChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ProductPackagingEditorViewModel.HasChanges) or nameof(ProductPackagingEditorViewModel.IsLoaded)) NotifyDocumentState(); }
    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(IsBusy) or nameof(CanWrite)) { OnPropertyChanged(nameof(CanEditHeader)); OnPropertyChanged(nameof(CanEditTags)); Packaging.CanEdit = CanEditHeader; } }
    /// <summary>身份或权限变化使旧请求失效，冻结写入并保留编辑。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        Interlocked.Increment(ref _sessionSequence); _loadCts?.Cancel(); _supplierCts?.Cancel(); _manufacturerCts?.Cancel();
        _masterLookupCts?.Cancel(); _neutralLookupCts?.Cancel();
        ApplyPermission(); NotifyDocumentState();
    }
    private void ApplyPermission() => IsReadOnly = _currentUser is not null && !_currentUser.HasPermission(PermissionKeys.ProductsProductEdit);
    private bool IsAlive(long session) => !_disposed && session == Volatile.Read(ref _sessionSequence) && CanRead();
    private bool CanRead() => _currentUser is null || _currentUser.HasPermission(PermissionKeys.ProductsProductView);
    private bool CanReadCommand() => !_disposed && IsNotBusy && CanRead();
    private bool CanEditLookup() => CanEditHeader;
    private bool CanSave() => !_disposed && Header.Uid != Guid.Empty && CanWrite && IsNotBusy && !Tags.IsPopupOpen;
    /// <summary>文档脏标记镜像资料、选项和包装状态，更新标题与保存入口。</summary>
    private void NotifyDocumentState()
    {
        if (_disposed) return;
        IsDirty = HasUnsavedChanges; Title = DocumentTitle;
        OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(TemplateStatus)); OnPropertyChanged(nameof(SourceCategoryName));
        OnPropertyChanged(nameof(CanEditHeader)); OnPropertyChanged(nameof(CanEditTags));
        Packaging.CanEdit = CanEditHeader;
        SaveCommand.NotifyCanExecuteChanged();
    }
    /// <summary>对称取消读取、解除事件并释放私有工作区，晚到响应不能回写。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Interlocked.Increment(ref _sessionSequence);
        Header.PropertyChanged -= OnHeaderChanged; Tags.StateChanged -= OnTagsChanged; PropertyChanged -= OnDocumentChanged;
        Packaging.PropertyChanged -= OnPackagingChanged; Packaging.Dispose();
        if (_cache is not null) _cache.ProductAttributesChanged -= OnAttributesChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        _lifetimeCts.Cancel();
        foreach (var cts in new[] { _loadCts, _supplierCts, _manufacturerCts, _masterLookupCts, _neutralLookupCts }) { cts?.Cancel(); cts?.Dispose(); }
        _lifetimeCts.Dispose(); Tags.Dispose(); CommitPendingEdits = null; RefreshEditorTargets = null;
    }
}
