using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>编排产品资料列表、批量保存及从已关联主型号拉取所选资料；实际仓库由单据页面决定。</summary>
public sealed partial class ProductsViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    private const int DefaultPageSize = 20;
    private readonly IProductApi _productApi;
    private readonly ICustomerApi _customerApi;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IReferenceDataCacheService _referenceDataCache = null!;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IProductImageUploadDialogService _imageUploadDialog;
    private readonly ICurrentUserService? _currentUser;
    private readonly INavigationService? _navigation;
    private readonly IProductMasterApi? _productMasterApi;
    private readonly IProductNeutralManagerApi? _productNeutralApi;
    private CancellationTokenSource? _masterLookupCts, _neutralLookupCts;
    private readonly Dictionary<Guid, CancellationTokenSource> _detailRefreshes = [];
    private bool _disposed;
    private bool _isInitialized;
    private bool _restoringSelection;
    private long _supplierSearchVersion;

    /// <summary>注入产品查询维护、资料候选及独立图片上传弹窗，初始化页面命令。</summary>
    public ProductsViewModel(
        IProductApi productApi,
        ICustomerApi customerApi,
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        IReferenceDataCacheService referenceDataCache,
        IMessageService messageService,
        IDialogService dialogService,
        BomDrawerViewModel bomDrawer,
        IProductImageUploadDialogService imageUploadDialog,
        ICurrentUserService? currentUser = null,
        INavigationService? navigation = null,
        IProductMasterApi? productMasterApi = null,
        IProductNeutralManagerApi? productNeutralApi = null)
    {
        _productApi = productApi;
        _customerApi = customerApi;
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        _referenceDataCache = referenceDataCache!;
        _messageService = messageService;
        _dialogService = dialogService;
        _imageUploadDialog = imageUploadDialog;
        _currentUser = currentUser;
        _navigation = navigation;
        _productMasterApi = productMasterApi;
        _productNeutralApi = productNeutralApi;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        BomDrawer = bomDrawer;
        Title = "物料管理";
        Products.PropertyChanged += OnProductsPropertyChanged;
        Products.ListStateChanged += OnProductsListStateChanged;
        InitializeCheckSelection();
        BomDrawer.SelectionRestoreRequested += OnBomDrawerSelectionRestoreRequested;
        if (_referenceDataCache?.DictionaryValues is { } dictionaryValues)
            CollectionChangedEventManager.AddHandler(
                dictionaryValues,
                OnDictionaryValuesChanged);
        RefreshUnitOptions();
        WeakReferenceMessenger.Default.Register<ProductsViewModel, ProductDetailSavedMessage>(this,
            static (recipient, message) => recipient.ObserveDetailSaved(message));
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(OpenDetailCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectProductChangesCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(UploadImagesCommand);
        RegisterBusyAwareCommand(PullMasterDescriptionsCommand);
        RegisterBusyAwareCommand(PullMasterFeaturesCommand);
        RegisterBusyAwareCommand(PullMasterSupplierCommand);
        RegisterBusyAwareCommand(PullMasterWeightVolumeCommand);
        RegisterBusyAwareCommand(PullMasterAllCommand);
    }

    /// <summary>打开独立图片上传用例，不保存或刷新当前产品表格中的未保存编辑。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenImageUpload))]
    private Task UploadImagesAsync() => RunBusyAsync(() => _imageUploadDialog.ShowAsync(), "产品主图上传");

    private bool CanOpenImageUpload() => IsNotBusy;

    /// <summary>从关联主型号拉取中英文描述。</summary>
    [RelayCommand(CanExecute = nameof(CanPullMasterData))]
    private Task PullMasterDescriptionsAsync() => PullMasterDataAsync("中英文描述", request => _productApi.PullMasterDescriptionsAsync(request));

    /// <summary>从关联主型号拉取中英文配置。</summary>
    [RelayCommand(CanExecute = nameof(CanPullMasterData))]
    private Task PullMasterFeaturesAsync() => PullMasterDataAsync("中英文配置", request => _productApi.PullMasterFeaturesAsync(request));

    /// <summary>从关联主型号拉取默认供应商。</summary>
    [RelayCommand(CanExecute = nameof(CanPullMasterData))]
    private Task PullMasterSupplierAsync() => PullMasterDataAsync("默认供应商", request => _productApi.PullMasterSupplierAsync(request));

    /// <summary>从关联主型号直接拉取毛重和体积到产品单位字段。</summary>
    [RelayCommand(CanExecute = nameof(CanPullMasterData))]
    private Task PullMasterWeightVolumeAsync() => PullMasterDataAsync("毛重和体积", request => _productApi.PullMasterWeightVolumeAsync(request));

    /// <summary>从关联主型号拉取全部四组资料。</summary>
    [RelayCommand(CanExecute = nameof(CanPullMasterData))]
    private Task PullMasterAllAsync() => PullMasterDataAsync("中英文描述、中英文配置、默认供应商、毛重和体积", request => _productApi.PullMasterAllAsync(request));

    /// <summary>确认所选范围后发起单次拉取；成功时刷新数据库现值，取消时不发送请求。</summary>
    private Task PullMasterDataAsync(string fields, Func<PullProductMasterDataRequest, Task<ServiceResult<PullProductMasterDataResponse>>> send)
    {
        if (!CanPullMasterData()) return Task.CompletedTask;
        var ids = SelectedPullLines().Select(line => line.Uid).Distinct().ToArray();
        return RunBusyAsync(async () =>
        {
            if (!await _dialogService.ConfirmAsync("从主型号拉取资料",
                $"将从数据库中已保存的主型号把{fields}覆盖到所选 {ids.Length} 个产品；无可用主型号的产品会跳过。是否继续？", "拉取")) return;
            if (_disposed || IsReadOnly || Products.HasChanges || _currentUser?.HasPermission(PermissionKeys.ProductsProductEdit) != true
                || !ids.SequenceEqual(SelectedPullLines().Select(line => line.Uid).Distinct())) return;

            var result = await _apiCallRunner.RunAsync(() => send(new PullProductMasterDataRequest { ProductIds = ids.ToList() }), "从主型号拉取资料失败");
            if (_disposed) return;
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("从主型号拉取资料失败", result.Message);
                return;
            }
            _messageService.ShowSuccess("主型号资料已拉取",
                $"已选 {result.Data.SelectedProductCount} 个产品，跳过 {result.Data.SkippedProductCount} 个，实际更新 {result.Data.UpdatedProductCount} 个。");
            if (!await LoadPageCoreAsync())
                _messageService.ShowWarning("拉取成功", "资料已拉取，但列表刷新失败。请稍后手动刷新。");
        }, "正在从主型号拉取资料...");
    }

    /// <summary>勾选行优先，无勾选时使用当前行。</summary>
    private IReadOnlyList<ProductLineModel> SelectedPullLines() => Products.CheckedItems.Count > 0
        ? Products.CheckedItems : SelectedLine is null ? [] : [SelectedLine];

    /// <summary>只允许有产品编辑权限、无草稿且所选均为已保存产品时拉取。</summary>
    private bool CanPullMasterData() => !_disposed && IsNotBusy && !IsReadOnly && !Products.HasChanges
        && _currentUser?.HasPermission(PermissionKeys.ProductsProductEdit) == true
        && SelectedPullLines() is { Count: > 0 } lines
        && lines.All(line => !line.IsNew && !line.IsDeleted && line.Uid != Guid.Empty);

    /// <summary>刷新五个资料拉取入口的可执行状态。</summary>
    private void NotifyPullCommands()
    {
        PullMasterDescriptionsCommand.NotifyCanExecuteChanged();
        PullMasterFeaturesCommand.NotifyCanExecuteChanged();
        PullMasterSupplierCommand.NotifyCanExecuteChanged();
        PullMasterWeightVolumeCommand.NotifyCanExecuteChanged();
        PullMasterAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>权限快照变化时即时更新拉取入口。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        RefreshCheckState();
        _masterLookupCts?.Cancel(); _neutralLookupCts?.Cancel();
        ProductMasterCandidates.Clear(); ProductNeutralCandidates.Clear();
        EditProductMaster = null; EditProductNeutral = null;
        NotifyPullCommands();
        OpenDetailCommand.NotifyCanExecuteChanged();
    }

    /// <summary>由视图提交物料表格的原生单元格与行编辑；不提交或撤销 BOM 抽屉草稿。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }

    public ProductListModel Products { get; } = new();
    public BomDrawerViewModel BomDrawer { get; }
    public IReferenceDataCacheService ReferenceDataCache => _referenceDataCache;
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    /// <summary>独立主型号搜索候选，不参与物料的中性产品筛选。</summary>
    public ObservableCollection<ProductMasterLookupItemModel> ProductMasterCandidates { get; } = [];
    /// <summary>型号输入的中性产品编码建议，不建立物料引用关系。</summary>
    public ObservableCollection<ProductNeutralLookupItemModel> ProductNeutralCandidates { get; } = [];
    /// <summary>产品模块独立授权的精简仓库候选；不借用库存模块的仓库缓存。</summary>
    public ObservableCollection<WarehouseSitePickerDto> WarehouseCandidates { get; } = [];
    public IReadOnlyList<string> MaterialTypes { get; } = MaterialTypesEnum.MaterialTypes;

    [ObservableProperty]
    public partial IReadOnlyList<SysDictDataDto> UnitOptions { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<string> FilterMaterialTypes { get; set; } = [];
    public IReadOnlyList<string> SupplyTypes { get; } = SupplyTypeEnums.SupplyTypes;

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    public ObservableCollection<CustomerDto> FilterCustomerList { get; set; } = new();


    public ObservableCollection<ProductColorDto> FilterColorList { get; set; } = new();

    /// <summary>分类多选筛选草稿，由控件确定后更新；节点来自共享只读缓存。</summary>
    public ObservableCollection<ProductCategoryCacheNode> FilterCategories { get; } = [];


    [ObservableProperty]
    public partial ProductColorDto? EditColor { get; set; }
    [ObservableProperty]
    public partial SupplierDto? EditSupplier { get; set; }
    /// <summary>待确认的主型号候选，原行身份仅在确认或明确清空时变化。</summary>
    [ObservableProperty] public partial ProductMasterLookupItemModel? EditProductMaster { get; set; }
    /// <summary>待确认的型号编码建议，不回写主型号或颜色。</summary>
    [ObservableProperty] public partial ProductNeutralLookupItemModel? EditProductNeutral { get; set; }
    /// <summary>主型号候选读取状态。</summary>
    [ObservableProperty] public partial bool IsProductMasterLookupBusy { get; private set; }
    /// <summary>中性产品候选读取状态。</summary>
    [ObservableProperty] public partial bool IsProductNeutralLookupBusy { get; private set; }
    [ObservableProperty] public partial SupplierDto? EditManufacturer { get; set; }
    [ObservableProperty] public partial WarehouseSitePickerDto? EditWarehouse { get; set; }

    [ObservableProperty]
    public partial bool IsSupplierLookupBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    public partial ProductLineModel? SelectedLine { get; set; }

    /// <summary>格子模式只编辑单元格，不提供整行选区勾选、删除或详情入口。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    public partial bool IsCellSelectionMode { get; set; }

    [ObservableProperty]
    public partial bool IsComplexFieldMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = DefaultPageSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载产品资料。";

    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    public bool HasChanges => Products.HasChanges;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        var cacheResult = await _referenceDataCache.EnsureLoadedAsync();
        if (!cacheResult.Success)
        {
            _messageService.ShowError("加载基础资料失败", cacheResult.Message);
        }
        await LoadProductWarehouseCandidatesAsync();
        await LoadPageAsync(false);
    }

    partial void OnSelectedLineChanged(ProductLineModel? value)
    {
        Products.SelectedItem = value;
        if (!_restoringSelection) BomDrawer.NotifyProductSelection(value);
        NotifyPullCommands();
    }

    partial void OnPageIndexChanged(int value) => PageChangeCommand.NotifyCanExecuteChanged();
    partial void OnPageSizeChanged(int value) => PageChangeCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null) Keyword = query;
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = 1;
        await LoadPageAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task<bool> RefreshAsync() => LoadPageAsync(true);

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        SelectedLine = Products.AddNewProduct();
        UpdateChangeState();
    }

    /// <summary>保留勾选优先的删除范围；只在整行模式下提交编辑并确认既有行删除。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDeleteSelected() || !TryCommitPendingEdits()) return;
        var checkedLines = Products.CheckedItems;
        var lines = checkedLines.Count > 0
            ? checkedLines.ToArray()
            : SelectedLine is null
                ? []
                : [SelectedLine];
        if (lines.Length == 0) return;

        var persistedLines = lines.Where(line => !line.IsNew).ToArray();
        if (persistedLines.Length > 0)
        {
            var message = lines.Length == 1
                ? $"确定删除产品 {lines[0].Sku} 吗？保存前可以撤销。"
                : $"确定删除已勾选的 {lines.Length} 个产品吗？其中 {persistedLines.Length} 个已保存产品将在下次保存时删除。";
            if (!await _dialogService.ConfirmAsync("删除产品", message, "删除")) return;
        }

        if (!CanDeleteSelected()) return;
        foreach (var line in lines)
        {
            Products.Remove(line);
        }

        SelectedLine = SelectedLine is not null && Products.Items.Contains(SelectedLine)
            ? SelectedLine
            : Products.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanRejectChanges))]
    private void RejectProductChanges()
    {
        Products.RejectChanges();
        SelectedLine = Products.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var error = await Products.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("产品资料未保存", error);
                return;
            }

            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(() => _productApi.BatchSaveAsync(request), "保存产品资料失败");
            if (!result.Success)
            {
                _messageService.ShowError("保存产品资料失败", result.Message);
                return;
            }

            // 只有明确成功的物料回执可以建立新绑定基线，不能仅凭外层请求成功锁定编号。
            var savedIds = result.Data?.Items.Where(item => item.Success).Select(item => item.Uid).ToHashSet() ?? [];
            foreach (var product in Products.Items.Where(product => savedIds.Contains(product.Uid))) product.AcceptBomBindingBaseline();
            Products.AcceptChanges();
            UpdateChangeState();
            _messageService.ShowSuccess("产品资料已保存", $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
            await LoadPageCoreAsync();
        }, "正在保存产品资料...");
    }

    /// <summary>打开已保存物料详情；仅保存或放弃目标行，保留其他物料及 BOM 草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private async Task OpenDetailAsync()
    {
        var target = SelectedLine;
        if (!CanOpenDetail() || target is null || !TryCommitPendingEdits()) return;
        await RunBusyAsync(async () =>
        {
            if (target.IsDirty)
            {
                var choice = await _dialogService.ShowAsync("当前物料有未保存修改。这里只处理当前行，其他物料及 BOM 抽屉草稿会保留。",
                    new DialogOptions("打开物料详情")
                    {
                        PrimaryButtonText = CanEditCurrentProduct() ? "保存当前行并打开" : null,
                        SecondaryButtonText = "放弃当前行并打开",
                        CloseButtonText = "取消"
                    });
                if (_disposed || !Products.Items.Contains(target) || target.IsDeleted || choice == DialogResult.Close) return;
                if (choice == DialogResult.Primary)
                {
                    if (!CanEditCurrentProduct() || !await SaveDetailTargetCoreAsync(target)) return;
                }
                else if (choice == DialogResult.Secondary)
                {
                    target.RejectChanges();
                    UpdateChangeState();
                }
            }
            if (_disposed || target.IsDirty || !Products.Items.Contains(target) || target.IsDeleted
                || !HasProductViewPermission() || _navigation is null) return;
            await _navigation.OpenDocumentAsync(ProductDetailViewModel.RouteKey, $"product:{target.Uid:N}",
                new ProductDocumentOpenRequest { ProductId = target.Uid });
        }, "正在打开物料详情...");
    }

    /// <summary>独立提交目标物料的更新与版本，任何失败均不接纳其他行或整张表的草稿。</summary>
    private async Task<bool> SaveDetailTargetCoreAsync(ProductLineModel target)
    {
        if (!TryCommitPendingEdits() || target.IsNew || target.IsDeleted || !Products.Items.Contains(target)) return false;
        var validation = target.ValidateRow() ?? await target.ValidateAsync();
        if (!string.IsNullOrWhiteSpace(validation))
        {
            _messageService.ShowWarning("物料未保存", validation);
            return false;
        }
        if (_disposed || !CanEditCurrentProduct()) return false;
        var request = new ProductBatchSaveRequest
        {
            UpdateItems = [new UpdateItemRequest<ProductDto> { Uid = target.Uid, Version = target.Version, Data = target.ToDto() }]
        };
        var result = await _apiCallRunner.RunAsync(() => _productApi.BatchSaveAsync(request), "保存当前物料失败");
        if (_disposed) return false;
        var receipt = result.Data?.Items.SingleOrDefault(item => item.Uid == target.Uid && item.Operation == SaveOperation.Update);
        if (!result.Success || receipt?.Success != true || receipt.NewVersion is not > 0)
        {
            _messageService.ShowError("保存当前物料失败", receipt?.Message ?? result.Message ?? "服务端未返回有效保存回执。请核对后重试。");
            return false;
        }
        if (!Products.Items.Contains(target) || target.IsDeleted) return false;
        target.Version = receipt.NewVersion.Value;
        target.AcceptBomBindingBaseline();
        target.AcceptChanges();
        UpdateChangeState();
        await RefreshSavedDetailAsync(target.Uid);
        return !_disposed && !target.IsDirty;
    }

    /// <summary>提交原生编辑失败时保留缓冲及焦点，阻止详情入口读取未提交值。</summary>
    private bool TryCommitPendingEdits()
    {
        if (CommitPendingEdits?.Invoke() != false) return true;
        ResultSummary = "当前单元格无法提交，请修正输入后再打开或刷新物料详情。";
        return false;
    }

    /// <summary>读取权限不要求编辑权限；格子模式、新增、删除、Busy 或释放状态不得进入详情。</summary>
    private bool CanOpenDetail() => !_disposed && !IsCellSelectionMode && IsNotBusy && _navigation is not null && HasProductViewPermission()
        && SelectedLine is { IsNew: false, IsDeleted: false } row && row.Uid != Guid.Empty;

    private bool HasProductViewPermission() => _currentUser is null || _currentUser.HasPermission(PermissionKeys.ProductsProductView);
    private bool CanEditCurrentProduct() => CanWrite && (_currentUser is null || _currentUser.HasPermission(PermissionKeys.ProductsProductEdit));

    /// <summary>监听详情提交，后续查询只更新对应列表行，不触碰 BOM 抽屉。</summary>
    private void ObserveDetailSaved(ProductDetailSavedMessage message)
    {
        if (!_disposed && message.ProductId != Guid.Empty) _ = RefreshSavedDetailAsync(message.ProductId);
    }

    /// <summary>按物料身份仅接纳最新详情投影；查询期间的新草稿、替换行和释放页面均不回写。</summary>
    private async Task RefreshSavedDetailAsync(Guid productId)
    {
        CancellationTokenSource? current = null;
        try
        {
            if (_detailRefreshes.Remove(productId, out var previous)) { previous.Cancel(); previous.Dispose(); }
            var target = Products.Items.FirstOrDefault(row => row.Uid == productId);
            if (target is null || _disposed) return;
            if (!TryCommitPendingEdits() || target.IsDirty)
            {
                ResultSummary = "物料详情已保存；对应列表行仍有草稿，请处理草稿后刷新。";
                return;
            }
            current = new CancellationTokenSource();
            _detailRefreshes[productId] = current;
            var result = await _apiCallRunner.RunAsync(() => _productApi.GetDetailAsync(productId, current.Token), current.Token, "刷新物料详情失败");
            if (_disposed || current.IsCancellationRequested || !_detailRefreshes.TryGetValue(productId, out var owner)
                || !ReferenceEquals(owner, current)) return;
            if (!result.Success || result.Data is null || result.Data.Uid != productId)
            {
                ResultSummary = "物料详情已保存，但列表行刷新失败，请稍后手动刷新。";
                return;
            }
            if (!Products.Items.Contains(target) || !TryCommitPendingEdits() || target.IsDirty)
            {
                ResultSummary = "物料详情已保存；查询期间的列表草稿已保留，请处理后刷新。";
                return;
            }
            target.Load(result.Data.Adapt<ProductDto>());
            UpdateChangeState();
        }
        catch (OperationCanceledException) when (current?.IsCancellationRequested == true) { }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("物料详情保存后的列表投影接纳失败：{0}", exception);
            if (!_disposed && current?.IsCancellationRequested != true) ResultSummary = "物料详情已保存；列表刷新未完成，请稍后手动刷新。";
        }
        finally
        {
            if (current is not null && _detailRefreshes.TryGetValue(productId, out var owner) && ReferenceEquals(owner, current))
                _detailRefreshes.Remove(productId);
            current?.Dispose();
        }
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        Keyword = string.Empty;
        FilterCustomerList.Clear();
        FilterColorList.Clear();
        FilterCategories.Clear();
        FilterMaterialTypes.Clear();
        PageIndex = 1;
        await LoadPageAsync(false);
    }

    [RelayCommand]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        return GoToPageAsync(request.NewIndex, request.NewSize);
    }

    [RelayCommand] private Task SearchSuppliersAsync(string? keyword) => LoadSuppliersAsync(keyword);

    /// <summary>主型号搜索取消前一次请求；页面释放或新查询启动后不接纳旧候选。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductMastersAsync(string? keyword)
    {
        if (_disposed || !CanWrite || IsBusy || _productMasterApi is null) return;
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _masterLookupCts, current); previous?.Cancel(); previous?.Dispose();
        IsProductMasterLookupBusy = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(() => _productMasterApi.GetPagedAsync(
                new ProductMasterSearchRequest { Keyword = NullIfWhiteSpace(keyword), PageIndex = 1, PageSize = 50 }, current.Token),
                current.Token, "搜索主型号失败");
            if (_disposed || current.IsCancellationRequested || !ReferenceEquals(current, _masterLookupCts)) return;
            if (!result.Success) { _messageService.ShowWarning("主型号候选未加载", result.Message); return; }
            ProductMasterCandidates.Clear();
            foreach (var item in result.Data?.Items ?? []) ProductMasterCandidates.Add(new(item));
        }
        finally { if (!_disposed && ReferenceEquals(current, _masterLookupCts)) IsProductMasterLookupBusy = false; }
    }

    /// <summary>查询中性产品编码建议；自由输入不要求命中候选或所选主型号。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductNeutralsAsync(string? keyword)
    {
        if (_disposed || !CanWrite || IsBusy || _productNeutralApi is null) return;
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _neutralLookupCts, current); previous?.Cancel(); previous?.Dispose();
        IsProductNeutralLookupBusy = true;
        try
        {
            var result = await _apiCallRunner.RunAsync(() => _productNeutralApi.GetPagedAsync(
                new ProductNeutralManagerSearchRequest { Keyword = NullIfWhiteSpace(keyword), PageIndex = 1, PageSize = 50 }, current.Token),
                current.Token, "搜索中性产品失败");
            if (_disposed || current.IsCancellationRequested || !ReferenceEquals(current, _neutralLookupCts)) return;
            if (!result.Success) { _messageService.ShowWarning("中性产品候选未加载", result.Message); return; }
            ProductNeutralCandidates.Clear();
            foreach (var item in result.Data?.Items ?? []) ProductNeutralCandidates.Add(new(item));
        }
        finally { if (!_disposed && ReferenceEquals(current, _neutralLookupCts)) IsProductNeutralLookupBusy = false; }
    }

    /// <summary>确认主型号仅保存自身引用与联表显示，不改变物料自身型号、颜色或独立资料。</summary>
    [RelayCommand]
    private void CommitProductMaster(ProductLineModel? line)
    {
        if (_disposed || !CanWrite || IsBusy || line is null || EditProductMaster is null) return;
        ApplyProductMaster(line, EditProductMaster.Uid, EditProductMaster.MasterSku ?? string.Empty, EditProductMaster.CategoryId, EditProductMaster.CategoryName ?? string.Empty);
        EditProductMaster = null;
    }
    /// <summary>明确清空主型号保存 null，物料自身型号及已有标签保持。</summary>
    [RelayCommand]
    private void ClearProductMaster(ProductLineModel? line)
    {
        if (_disposed || !CanWrite || IsBusy || line is null) return;
        line.ProductMasterId = null; line.MasterSku = string.Empty; line.CategoryId = Guid.Empty; line.CategoryName = string.Empty;
        EditProductMaster = null;
    }
    /// <summary>确认候选仅填写物料自身型号，不从候选推导主型号或颜色。</summary>
    [RelayCommand]
    private void CommitProductNeutral(ProductLineModel? line)
    {
        if (_disposed || !CanWrite || IsBusy || line is null || EditProductNeutral is null) return;
        line.LinkSku = EditProductNeutral.ProductCode ?? string.Empty;
        EditProductNeutral = null;
    }
    /// <summary>明确清空物料自身型号，其余关系不变。</summary>
    [RelayCommand]
    private void ClearProductNeutral(ProductLineModel? line)
    {
        if (_disposed || !CanWrite || IsBusy || line is null) return;
        line.LinkSku = string.Empty; EditProductNeutral = null;
    }

    [RelayCommand]
    private void CommitColor(ProductLineModel? line)
    {
        if (line is null || EditColor is null) return;
        ApplyColor(line, EditColor.Uid, EditColor.ColorNameZh ?? EditColor.ColorNameEn ?? string.Empty);
        EditColor = null;
    }

    [RelayCommand]
    private void ClearColor(ProductLineModel? line)
    {
        if (line is null) return;
        line.ColorId = Guid.Empty;
        line.Finish = string.Empty;
        EditColor = null;
    }

    [RelayCommand]
    private void CommitSupplier(ProductLineModel? line)
    {
        if (line is null || EditSupplier is null) return;
        ApplySupplier(line, EditSupplier.Uid, EditSupplier.SupplierCode ?? string.Empty, EditSupplier.SupplierName ?? string.Empty);
        EditSupplier = null;
    }

    [RelayCommand]
    private void ClearSupplier(ProductLineModel? line)
    {
        if (line is null) return;
        line.DefaultSupplierId = null;
        line.DefaultSupplierCode = string.Empty;
        line.SupplierName = string.Empty;
        EditSupplier = null;
    }

    /// <summary>记录当前产品的制造商，不触碰默认采购供应商及独立型号。</summary>
    [RelayCommand]
    private void CommitManufacturer(ProductLineModel? line)
    {
        if (line is null || EditManufacturer is null) return;
        ApplyManufacturer(line, EditManufacturer.Uid, EditManufacturer.SupplierCode ?? string.Empty, EditManufacturer.SupplierName ?? string.Empty);
        EditManufacturer = null;
    }

    /// <summary>显式清空制造商；已录入的制造商型号保持原值。</summary>
    [RelayCommand]
    private void ClearManufacturer(ProductLineModel? line)
    {
        if (line is null) return;
        line.ManufacturerId = null;
        line.ManufacturerCode = string.Empty;
        line.ManufacturerName = string.Empty;
        EditManufacturer = null;
    }

    /// <summary>更新产品参考仓库，不改任何出入库单头仓库。</summary>
    [RelayCommand]
    private void CommitWarehouse(ProductLineModel? line)
    {
        if (line is null || EditWarehouse is null) return;
        ApplyWarehouse(line, EditWarehouse.Uid, EditWarehouse.Code ?? string.Empty, EditWarehouse.Name ?? string.Empty);
        EditWarehouse = null;
    }

    /// <summary>显式清空产品参考仓库，不触碰其他产品资料。</summary>
    [RelayCommand]
    private void ClearWarehouse(ProductLineModel? line)
    {
        if (line is null) return;
        line.DefaultWarehouseSiteId = null;
        line.DefaultWarehouseCode = string.Empty;
        line.DefaultWarehouseName = string.Empty;
        EditWarehouse = null;
    }

    /// <summary>逐页读取产品权限下的活动仓库，完整成功后才发布本地选择集合。</summary>
    private async Task LoadProductWarehouseCandidatesAsync()
    {
        const int pageSize = 200;
        var loaded = new List<WarehouseSitePickerDto>();
        for (var pageIndex = 1; ; pageIndex++)
        {
            var request = new PagedRequestBase { PageIndex = pageIndex, PageSize = pageSize };
            var result = await _apiCallRunner.RunAsync(
                () => _productApi.GetDefaultWarehouseCandidatesAsync(request), "加载产品默认仓库候选失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowWarning("仓库候选未加载", result.Message);
                return;
            }
            loaded.AddRange(result.Data.Items.Select(item => new WarehouseSitePickerDto
            {
                Uid = item.Uid,
                Code = item.Code,
                Name = item.Name,
                IsActive = true
            }));
            if (loaded.Count >= result.Data.TotalCount || result.Data.Items.Count == 0) break;
        }
        WarehouseCandidates.Clear();
        foreach (var warehouse in loaded) WarehouseCandidates.Add(warehouse);
    }

    private async Task<bool> LoadPageAsync(bool confirmChanges)
    {
        if (confirmChanges && !await EnsureCanDiscardChangesAsync()) return false;
        var loaded = false;
        await RunBusyAsync(async () => loaded = await LoadPageCoreAsync(), "正在加载产品资料...");
        return loaded;
    }

    private async Task<bool> LoadPageCoreAsync()
    {
        var request = new ProductSearchRequest
        {
            Keyword = NullIfWhiteSpace(Keyword),
            CustomerIds = FilterCustomerList is null ? [] : FilterCustomerList.Select(x => x.Uid).ToList(),
            CategoryId = Guid.Empty,
            CategoryIds = FilterCategories.SelectMany(selected =>
                    GetCategoryIds(_referenceDataCache.ProductCategories.FirstOrDefault(node => node.Uid == selected.Uid) ?? selected))
                .Where(id => id != Guid.Empty).Distinct().ToList(),
            ColorIds = FilterColorList is null ? [] : FilterColorList.Select(x => x.Uid).ToList(),
            MaterialTypeList = FilterMaterialTypes.ToList(),
            PageIndex = PageIndex,
            PageSize = PageSize,
        };
        var result = await _apiCallRunner.RunAsync(() => _productApi.GetPagedAsync(request), "加载产品资料失败");
        if (!result.Success)
        {
            ResultSummary = "产品资料加载失败。";
            _messageService.ShowError("加载产品资料失败", result.Message);
            return false;
        }

        var page = result.Data;
        Products.LoadFromDtos(page?.Items ?? []);
        SelectedLine = Products.Items.FirstOrDefault();
        TotalCount = page?.TotalCount ?? 0; PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
        PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
        ResultSummary = $"共 {TotalCount} 条，本页 {Products.Items.Count} 条。";
        AcceptChanges(); UpdateChangeState();
        return true;
    }

    private async Task GoToPageAsync(int index, int size)
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = Math.Max(1, index); PageSize = size <= 0 ? DefaultPageSize : size;
        await LoadPageAsync(false);
    }


    private async Task LoadSuppliersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _supplierSearchVersion);

        IsSupplierLookupBusy = true;

        var result = await _apiCallRunner.RunAsync(() => _supplierApi.GetPagedAsync(new SupplierSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 30 }), "搜索供应商失败");
        if (version == Volatile.Read(ref _supplierSearchVersion)) SupplierCandidates.ReplaceWith(result.Success ? result.Data?.Items ?? [] : []);

        IsSupplierLookupBusy = false;
    }

    private async Task<bool> EnsureCanDiscardChangesAsync() =>
        !Products.HasChanges || await _dialogService.ConfirmAsync("放弃未保存变更", "当前产品列表有未保存修改，继续将放弃这些修改。", "继续");

    /// <summary>保留原分类筛选的后代范围；调用方合并多项并去重，父子重复勾选不重复提交。</summary>
    private static IEnumerable<Guid> GetCategoryIds(ProductCategoryCacheNode node)
    {
        yield return node.Uid;
        foreach (var child in node.Children)
        {
            foreach (var uid in GetCategoryIds(child))
            {
                yield return uid;
            }
        }
    }

    private void RestoreSelection(ProductLineModel? product)
    {
        _restoringSelection = true;
        try { SelectedLine = product; }
        finally { _restoringSelection = false; }
    }


    private void OnProductsPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        UpdateChangeState();

    /// <summary>勾选及行状态变化时更新拉取和需求入口；类型、编号、需求及已保存绑定变化同时刷新 BOM 父件资格。</summary>
    private void OnProductsListStateChanged(object? sender, ListStateChangedEventArgs<ProductLineModel> e)
    {
        UpdateChangeState();
        if (e.PropertyName is null or nameof(ProductLineModel.MaterialType) or nameof(ProductLineModel.BomNo) or nameof(ProductLineModel.BomVersion) or nameof(ProductLineModel.IsBomBindingSaved))
            BomDrawer.NotifyParentEligibilityChanged();
    }

    private void OnDictionaryValuesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshUnitOptions();

    private void RefreshUnitOptions() =>
        UnitOptions = _referenceDataCache?.GetDictionaryValues(DictCodes.Unit) ?? [];


    /// <summary>同步物料草稿、显示行勾选及现有业务入口，勾选不改变业务编辑基线。</summary>
    private void UpdateChangeState()
    {
        RefreshCheckState();
        IsDirty = Products.HasChanges; OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged(); RejectProductChangesCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged();
        OpenDetailCommand.NotifyCanExecuteChanged();
        NotifyPullCommands();
    }

    /// <summary>BOM 抽屉请求恢复选择时转发给产品列表，并允许页面释放时解除订阅。</summary>
    private void OnBomDrawerSelectionRestoreRequested(object? sender, ProductLineModel? product) => RestoreSelection(product);

    /// <summary>页面释放时解除产品列表和权限订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachCheckSelection();
        foreach (var lookup in new[] { _masterLookupCts, _neutralLookupCts }) { lookup?.Cancel(); lookup?.Dispose(); }
        WeakReferenceMessenger.Default.Unregister<ProductDetailSavedMessage>(this);
        foreach (var pending in _detailRefreshes.Values) { pending.Cancel(); pending.Dispose(); }
        _detailRefreshes.Clear();
        CommitPendingEdits = null;
        Products.PropertyChanged -= OnProductsPropertyChanged;
        Products.ListStateChanged -= OnProductsListStateChanged;
        BomDrawer.SelectionRestoreRequested -= OnBomDrawerSelectionRestoreRequested;
        if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        if (_referenceDataCache?.DictionaryValues is { } dictionaryValues)
            CollectionChangedEventManager.RemoveHandler(dictionaryValues, OnDictionaryValuesChanged);
    }


    private bool CanRun() => IsNotBusy;

    private bool CanWriteCommand() => IsNotBusy && CanWrite;

    /// <summary>整行模式沿用勾选优先的删除范围，释放或写入受限时禁止入口。</summary>
    private bool CanDeleteSelected() =>
        !_disposed && !IsCellSelectionMode && IsNotBusy && CanWrite && (Products.CheckedCount > 0 || SelectedLine is not null);

    private bool CanRejectChanges() => IsNotBusy && Products.HasChanges;

    private bool CanSave() => IsNotBusy && CanWrite && Products.HasChanges;

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>完整更新主型号关联事实，供候选确认和向下填充共同调用；不改变保存基线。</summary>
    private static void ApplyProductMaster(ProductLineModel target, Guid? productMasterId, string masterSku, Guid categoryId, string categoryName)
    {
        target.ProductMasterId = productMasterId;
        target.MasterSku = masterSku;
        target.CategoryId = categoryId;
        target.CategoryName = categoryName;
    }

    /// <summary>完整更新颜色关联事实，供候选确认和向下填充共同调用；不改变保存基线。</summary>
    private static void ApplyColor(ProductLineModel target, Guid colorId, string finish)
    {
        target.ColorId = colorId;
        target.Finish = finish;
    }

    /// <summary>完整更新供应商关联事实，供候选确认和向下填充共同调用；不改变保存基线。</summary>
    private static void ApplySupplier(ProductLineModel target, Guid? defaultSupplierId, string defaultSupplierCode, string supplierName)
    {
        target.DefaultSupplierId = defaultSupplierId;
        target.DefaultSupplierCode = defaultSupplierCode;
        target.SupplierName = supplierName;
    }

    /// <summary>完整更新制造方关联事实，供候选确认和向下填充共同调用；不改变保存基线。</summary>
    private static void ApplyManufacturer(ProductLineModel target, Guid? manufacturerId, string manufacturerCode, string manufacturerName)
    {
        target.ManufacturerId = manufacturerId;
        target.ManufacturerCode = manufacturerCode;
        target.ManufacturerName = manufacturerName;
    }

    /// <summary>完整更新仓库关联事实，供候选确认和向下填充共同调用；不改变保存基线。</summary>
    private static void ApplyWarehouse(ProductLineModel target, Guid? defaultWarehouseSiteId, string defaultWarehouseCode, string defaultWarehouseName)
    {
        target.DefaultWarehouseSiteId = defaultWarehouseSiteId;
        target.DefaultWarehouseCode = defaultWarehouseCode;
        target.DefaultWarehouseName = defaultWarehouseName;
    }

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => !_disposed && CanWrite && IsNotBusy
        && request is not null && request.Fields.All(field => field is nameof(ProductLineModel.ProductMasterId)
            or nameof(ProductLineModel.LinkSku)
            or nameof(ProductLineModel.DescriptionEn)
            or nameof(ProductLineModel.DescriptionZh)
            or nameof(ProductLineModel.FeaturesZh)
            or nameof(ProductLineModel.FeaturesEn)
            or nameof(ProductLineModel.ColorId)
            or nameof(ProductLineModel.MaterialType)
            or nameof(ProductLineModel.Unit)
            or nameof(ProductLineModel.SupplyType)
            or nameof(ProductLineModel.DefaultSupplierId)
            or nameof(ProductLineModel.ManufacturerId)
            or nameof(ProductLineModel.ManufacturerModel)
            or nameof(ProductLineModel.DefaultWarehouseSiteId)
            or nameof(ProductLineModel.CnyPrice)
            or nameof(ProductLineModel.UsdPrice)
            or nameof(ProductLineModel.PcsOfCarton)
            or nameof(ProductLineModel.NwOfCarton)
            or nameof(ProductLineModel.GwOfCarton)
            or nameof(ProductLineModel.CbmOfCarton)
            or nameof(ProductLineModel.Note))
        && request.TryGetRows<ProductLineModel>(Products.Items, out _, out var targets)
        && targets.Any(row => !row.IsDeleted && !row.IsReadOnly);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<ProductLineModel>(Products.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(row => !row.IsDeleted && !row.IsReadOnly))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(ProductLineModel.ProductMasterId): ApplyProductMaster(target, source.ProductMasterId, source.MasterSku, source.CategoryId, source.CategoryName); break;
                    case nameof(ProductLineModel.LinkSku): target.LinkSku = source.LinkSku; break;
                    case nameof(ProductLineModel.DescriptionEn): target.DescriptionEn = source.DescriptionEn; break;
                    case nameof(ProductLineModel.DescriptionZh): target.DescriptionZh = source.DescriptionZh; break;
                    case nameof(ProductLineModel.FeaturesZh): target.FeaturesZh = source.FeaturesZh; break;
                    case nameof(ProductLineModel.FeaturesEn): target.FeaturesEn = source.FeaturesEn; break;
                    case nameof(ProductLineModel.ColorId): ApplyColor(target, source.ColorId, source.Finish); break;
                    case nameof(ProductLineModel.MaterialType): target.MaterialType = source.MaterialType; break;
                    case nameof(ProductLineModel.Unit): target.Unit = source.Unit; break;
                    case nameof(ProductLineModel.SupplyType): target.SupplyType = source.SupplyType; break;
                    case nameof(ProductLineModel.DefaultSupplierId): ApplySupplier(target, source.DefaultSupplierId, source.DefaultSupplierCode, source.SupplierName); break;
                    case nameof(ProductLineModel.ManufacturerId): ApplyManufacturer(target, source.ManufacturerId, source.ManufacturerCode, source.ManufacturerName); break;
                    case nameof(ProductLineModel.ManufacturerModel): target.ManufacturerModel = source.ManufacturerModel; break;
                    case nameof(ProductLineModel.DefaultWarehouseSiteId): ApplyWarehouse(target, source.DefaultWarehouseSiteId, source.DefaultWarehouseCode, source.DefaultWarehouseName); break;
                    case nameof(ProductLineModel.CnyPrice): target.CnyPrice = source.CnyPrice; break;
                    case nameof(ProductLineModel.UsdPrice): target.UsdPrice = source.UsdPrice; break;
                    case nameof(ProductLineModel.PcsOfCarton): target.PcsOfCarton = source.PcsOfCarton; break;
                    case nameof(ProductLineModel.NwOfCarton): target.NwOfCarton = source.NwOfCarton; break;
                    case nameof(ProductLineModel.GwOfCarton): target.GwOfCarton = source.GwOfCarton; break;
                    case nameof(ProductLineModel.CbmOfCarton): target.CbmOfCarton = source.CbmOfCarton; break;
                    case nameof(ProductLineModel.Note): target.Note = source.Note; break;
                }
            }
        }
    }

}




internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

}
