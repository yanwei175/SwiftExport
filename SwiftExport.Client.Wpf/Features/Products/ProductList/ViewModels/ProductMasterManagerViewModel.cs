using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Services.Images;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using Mapster;
using CommunityToolkit.Mvvm.Messaging;
using SwiftExportClient.Features.Products.ProductMasterImport.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>主产品管理页模型，负责主型号筛选、列表编辑、详情导航，以及将已保存主型号资料独立下发到关联产品。</summary>
public sealed partial class ProductMasterManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>获取主产品管理导航路由键。</summary>
    public const string RouteKey = "product-master-manager";
    private readonly IProductMasterApi _api;
    private readonly IProductNeutralApi _neutralApi;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IProductMasterSyncDialogService _syncDialog;
    private readonly ICurrentUserService? _currentUser;
    private readonly INavigationService _navigation;
    private readonly IImageAddressResolver _imageAddresses;
    private CancellationTokenSource? _queryCts;
    private long _querySequence;
    private long _supplierSequence;
    private ProductCategoryFilterSelection _appliedCategoryFilter = ProductCategoryFilterSelection.AllCategories;
    private bool _initialized;
    private bool _disposed;
    private readonly Dictionary<Guid, CancellationTokenSource> _detailRefreshes = [];

    /// <summary>初始化主产品管理页依赖。</summary>
    public ProductMasterManagerViewModel(
        IProductMasterApi api,
        ISupplierApi supplierApi,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        IProductNeutralApi neutralApi, IImageAddressResolver imageAddresses,
        IProductMasterSyncDialogService syncDialog,
        ICurrentUserService? currentUser = null)
    {
        _api = api;
        _neutralApi = neutralApi;
        _supplierApi = supplierApi;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _syncDialog = syncDialog;
        _currentUser = currentUser;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        _navigation = navigation;
        _imageAddresses = imageAddresses;
        _imageAddresses.Changed += OnImageConfigurationChanged;
        Title = "父级产品";
        Masters.ListStateChanged += OnListStateChanged;
        Neutrals.ListStateChanged += OnNeutralListStateChanged;
        PropertyChanged += OnManagerStateChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(OpenImportCommand);
        WeakReferenceMessenger.Default.Register<ProductMasterManagerViewModel, ProductMasterDetailSavedMessage>(this,
            static (recipient, message) => recipient.ObserveDetailSaved(message));
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(OpenDetailCommand);
        RegisterBusyAwareCommand(SyncDescriptionsCommand);
        RegisterBusyAwareCommand(SyncFeaturesCommand);
        RegisterBusyAwareCommand(SyncSupplierCommand);
        RegisterBusyAwareCommand(SyncWeightVolumeCommand);
        RegisterBusyAwareCommand(SyncAllCommand);
    }

    /// <summary>获取主型号编辑集合。</summary>
    public ProductMasterListModel Masters { get; } = new();
    /// <summary>获取基础资料缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache => _cache;
    /// <summary>获取供应商搜索候选。</summary>
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    /// <summary>获取分页总页数。</summary>
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    /// <summary>获取分页状态文本。</summary>
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    /// <summary>获取文档标题。</summary>
    public string DocumentTitle => Title;
    /// <summary>获取上下两张表是否存在未保存变更。</summary>
    public bool HasUnsavedChanges => Masters.HasChanges || Neutrals.HasChanges;

    /// <summary>获取或设置搜索关键词。</summary>
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    /// <summary>获取或设置共享分类筛选组件发布的选择快照。</summary>
    [ObservableProperty] public partial ProductCategoryFilterSelection CategoryFilter { get; set; } = ProductCategoryFilterSelection.AllCategories;
    /// <summary>获取或设置当前主型号行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductMasterLineModel? SelectedMaster { get; set; }
    /// <summary>获取或设置是否显示只读卡片视图。</summary>
    [ObservableProperty] public partial bool IsCardView { get; set; } = false;
    /// <summary>获取或设置是否显示英文描述与英文配置列。</summary>
    [ObservableProperty] public partial bool IsEnglishFieldsVisible { get; set; } = false;
    /// <summary>获取或设置 DataGrid 是否使用格子选择模式。</summary>
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; } = false;
    /// <summary>获取当前是否正在查询。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    public partial bool IsQuerying { get; set; }
    /// <summary>获取当前供应商 Lookup 是否正在查询。</summary>
    [ObservableProperty] public partial bool IsSupplierLookupBusy { get; set; }
    /// <summary>获取当前结果摘要。</summary>
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载主产品。";
    /// <summary>获取或设置分页总记录数。</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    /// <summary>获取或设置当前页码。</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    /// <summary>获取或设置每页记录数。</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;

    /// <summary>幂等加载基础资料和第一页主型号。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var cacheResult = await _cache.EnsureLoadedAsync();
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        await LoadPageAsync();
    }

    /// <summary>应用共享分类筛选快照；取消丢弃或查询失败时恢复最后一次成功应用的选择。</summary>
    [RelayCommand]
    private async Task ApplyCategoryFilterAsync(ProductCategoryFilterSelection? selection)
    {
        var requestedFilter = selection ?? ProductCategoryFilterSelection.AllCategories;
        if (AreCategoryFiltersEquivalent(requestedFilter, _appliedCategoryFilter))
        {
            CategoryFilter = _appliedCategoryFilter;
            return;
        }

        if (!await EnsureCanDiscardChangesAsync())
        {
            CategoryFilter = _appliedCategoryFilter;
            return;
        }

        CategoryFilter = requestedFilter;
        PageIndex = 1;
        if (!await LoadPageAsync()) CategoryFilter = _appliedCategoryFilter;
    }

    /// <summary>关闭页面前检查未保存变更。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => EnsureCanDiscardChangesAsync();

    partial void OnSelectedMasterChanged(ProductMasterLineModel? value)
    {
        Masters.SelectedItem = value;
        NotifySyncCommands();
        // 原生选择已变化、异步切换尚未完成时，不能继续编辑旧主型号的明细。
        NotifyNeutralCommands();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        await LoadPageAsync();
    }

    /// <summary>新增主型号前处理下表修改，新父记录保存前不允许新增明细。</summary>
    [RelayCommand]
    private async Task AddRowAsync()
    {
        if (!IsInteractionEnabled || !CanWrite || !TryCommitPendingEdits() || !await ConfirmNeutralDiscardAsync() || _disposed) return;
        SelectedMaster = Masters.AddNewMaster();
        ResetNeutralContext(SelectedMaster);
        UpdateChangeState();
    }

    [RelayCommand]
    private Task CreateDetailAsync() => _navigation.OpenDocumentAsync(
        ProductMasterDetailViewModel.RouteKey,
        $"product-master:new:{Guid.NewGuid():N}",
        new ProductMasterDocumentOpenRequest());

    /// <summary>沿用详情入口；先处理目标行草稿，取消或保存失败时不打开，不撤销其他型号的草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private async Task OpenDetailAsync(ProductMasterLineModel? line = null)
    {
        var target = line ?? SelectedMaster;
        if (!CanOpenDetail(target) || !TryCommitPendingEdits() || target is null) return;
        var id = target.Uid;
        var hasNeutralDraft = _neutralMaster?.Uid == id && Neutrals.HasChanges;
        if (target.IsDirty || hasNeutralDraft)
        {
            DialogResult choice;
            IsSelectionPrompt = true;
            try
            {
                choice = await _dialogs.ShowAsync(
                    "当前主型号或其中性产品有未保存修改。保存会提交当前列表的主型号变更及当前中性产品；放弃只撤销当前型号及其中性产品，其他型号草稿保留。",
                    new DialogOptions("打开主型号详情")
                    {
                        PrimaryButtonText = CanWrite ? "保存并打开" : null,
                        SecondaryButtonText = "放弃当前修改并打开",
                        CloseButtonText = "取消"
                    });
            }
            finally { IsSelectionPrompt = false; }
            if (_disposed || choice == DialogResult.Close) return;
            if (choice == DialogResult.Primary)
            {
                if (!CanWrite) return;
                if (Masters.HasChanges) await SaveAsync();
                if (!_disposed && _neutralMaster?.Uid == id && Neutrals.HasChanges) await SaveNeutralsAsync();
                if (_disposed || Masters.HasChanges) return;
                if (_neutralMaster?.Uid == id && Neutrals.HasChanges) return;
            }
            else
            {
                target.RejectChanges();
                if (_neutralMaster?.Uid == id && Neutrals.HasChanges) Neutrals.RejectChanges();
                UpdateChangeState();
            }
        }
        if (_disposed || !IsInteractionEnabled) return;
        await _navigation.OpenDocumentAsync(
            ProductMasterDetailViewModel.RouteKey,
            $"product-master:{id:N}",
            new ProductMasterDocumentOpenRequest { ProductMasterId = id });
    }

    /// <summary>新增行直接移除；涉及已保存行时确认软删除，完成后选择剩余最后一行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!IsInteractionEnabled || !CanWrite || !TryCommitPendingEdits()) return;
        var lines = Masters.CheckedItems.Count > 0
            ? Masters.CheckedItems.ToArray()
            : SelectedMaster is null ? [] : [SelectedMaster];
        if (lines.Length == 0) return;
        var last = Masters.Items.LastOrDefault(line => !lines.Contains(line));
        if (!ReferenceEquals(last, _neutralMaster) && !await ConfirmNeutralDiscardAsync()) return;
        IsSelectionPrompt = true;
        try
        {
            if (lines.Any(line => !line.IsNew)
                && !await _dialogs.ConfirmAsync("删除主型号", $"确定删除选中的 {lines.Length} 个主型号及其中性产品吗？保存后生效。", "删除")) return;
        }
        finally { IsSelectionPrompt = false; }
        if (_disposed) return;
        foreach (var line in lines) Masters.Remove(line);
        SelectedMaster = last;
        if (!ReferenceEquals(last, _neutralMaster)) ResetNeutralContext(last);
        UpdateChangeState();
    }

    /// <summary>保存主型号列表变更；先提交表格编辑并校验，再为新增或修改项收集本批次的物料同步选项。</summary>
    /// <remarks>取消确认或保存失败保留当前编辑；成功后回填行版本，下表有未保存草稿时跳过列表刷新。</remarks>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return;
        await RunWriteAsync(async () =>
        {
            var error = await Masters.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("主型号未保存", error);
                return;
            }

            // 行模型只组装主型号的新增、修改、删除；两个物料同步选项由本次确认结果单独赋值。
            var request = Masters.CreateSaveRequest();

            if (!request.HasChanges) return;

            if (request.CreateItems.Count > 0 || request.UpdateItems.Count > 0)
            {
                // 选项作用于本批次全部新增和修改项；纯删除不弹窗，也不会同步物料资料。
                var selection = await _syncDialog.ConfirmSaveAsync();
                if (!selection.Confirmed || _disposed) return;
                request.UpdateProductDescriptions = selection.UpdateProductDescriptions;
                request.UpdateProductDefaultSupplier = selection.UpdateProductDefaultSupplier;
            }

            // Refit POST /api/ProductMaster/batch-save；后端负责同事务写入主型号和关联物料。
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存主型号失败");

            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                _messages.ShowError("保存主型号失败", result.Message);
                return;
            }
            Masters.ApplySaveResponse(result.Data);
            UpdateChangeState();
            _messages.ShowSuccess("主型号已保存", $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
            // 下表有草稿时不替换上表集合；独立保存不能隐式丢弃另一张表的编辑。
            if (!Neutrals.HasChanges)
            {
                var refreshed = await LoadPageCoreAsync();
                if (!refreshed) _messages.ShowWarning("保存成功", "数据已保存，但列表刷新失败。请稍后手动刷新。");
            }
        }, "正在保存主型号...");
    }

    /// <summary>从已保存主型号下发中英文描述。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncDescriptionsAsync() => SyncSelectedAsync("中英文描述", request => _api.SyncDescriptionsAsync(request));

    /// <summary>从已保存主型号下发中英文配置。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncFeaturesAsync() => SyncSelectedAsync("中英文配置", request => _api.SyncFeaturesAsync(request));

    /// <summary>从已保存主型号下发默认供应商。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncSupplierAsync() => SyncSelectedAsync("默认供应商", request => _api.SyncSupplierAsync(request));

    /// <summary>将已保存主型号的毛重和体积直接下发到产品单位字段。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncWeightVolumeAsync() => SyncSelectedAsync("毛重和体积", request => _api.SyncWeightVolumeAsync(request));

    /// <summary>一次下发描述、配置、供应商、毛重和体积。</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncAllAsync() => SyncSelectedAsync("中英文描述、中英文配置、默认供应商、毛重和体积", request => _api.SyncAllAsync(request));

    /// <summary>确认下发范围并提交；页面草稿不参与数据库现值同步。</summary>
    private async Task SyncSelectedAsync(string fields, Func<SyncProductMasterDataRequest, Task<ServiceResult<SyncProductMasterDataResponse>>> send)
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits() || !CanSync()) return;
        var lines = Masters.CheckedItems.Count > 0 ? Masters.CheckedItems.ToArray()
            : SelectedMaster is null ? [] : [SelectedMaster];
        var request = new SyncProductMasterDataRequest { ProductMasterIds = lines.Select(line => line.Uid).Distinct().ToList() };
        IsSelectionPrompt = true;
        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync("下发主型号资料",
                $"将 {request.ProductMasterIds.Count} 个主型号数据库中已保存的{fields}覆盖到其未删除关联产品。是否继续？", "下发");
        }
        finally { IsSelectionPrompt = false; }
        if (!confirmed || _disposed || !CanSync()) return;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => send(request), "下发主型号资料失败");
            if (!result.Success || result.Data is null)
            {
                _messages.ShowError("下发主型号资料失败", result.Message);
                return;
            }
            _messages.ShowSuccess("主型号资料已下发", $"已检查 {result.Data.ProductMasterCount} 个主型号，更新 {result.Data.UpdatedProductCount} 个关联产品。");
        }, "正在下发主型号资料...");
    }

    [RelayCommand]
    private void RejectListChanges()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return;
        Masters.RejectChanges();
        SelectedMaster = _neutralMaster is not null && Masters.Items.Contains(_neutralMaster)
            ? _neutralMaster : Masters.Items.LastOrDefault();
        if (!ReferenceEquals(SelectedMaster, _neutralMaster)) ResetNeutralContext(SelectedMaster);
        UpdateChangeState();
    }

    [RelayCommand]
    private void ShowList() => IsCardView = false;

    [RelayCommand]
    private void ShowCards() => IsCardView = true;

    [RelayCommand]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await EnsureCanDiscardChangesAsync()) return;
        PageIndex = Math.Max(1, request.NewIndex);
        PageSize = request.NewSize <= 0 ? 20 : request.NewSize;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        var sequence = Interlocked.Increment(ref _supplierSequence);
        IsSupplierLookupBusy = true;
        var result = await _runner.RunAsync(() => _supplierApi.GetPagedAsync(new SupplierSearchRequest
        {
            Keyword = keyword,
            PageIndex = 1,
            PageSize = 30
        }), "搜索供应商失败");
        if (sequence == Volatile.Read(ref _supplierSequence))
        {
            SupplierCandidates.Clear();
            foreach (var item in result.Success ? result.Data?.Items ?? [] : []) SupplierCandidates.Add(item);
            IsSupplierLookupBusy = false;
        }
    }

    /// <summary>仅在确认候选分类时同步行标识和显示名称，不将搜索词作为业务数据。</summary>
    [RelayCommand]
    private void CommitCategory(ProductMasterLineModel? line)
    {
        if (line is null || EditCategory is null) return;
        line.CategoryId = EditCategory.Uid;
        line.CategoryName = EditCategory.CategoryName;
        EditCategory = null;
    }

    /// <summary>显式清空当前行分类；空 Guid 表示未设置，同时清除名称与临时选择。</summary>
    [RelayCommand]
    private void ClearCategory(ProductMasterLineModel? line)
    {
        if (line is null) return;
        line.CategoryId = Guid.Empty;
        line.CategoryName = string.Empty;
        EditCategory = null;
    }

    [RelayCommand]
    private void CommitSupplier(ProductMasterLineModel? line)
    {
        if (line is null || EditSupplier is null) return;
        line.DefaultSupplierId = EditSupplier.Uid;
        line.DefaultSupplierName = EditSupplier.SupplierName ?? string.Empty;
        EditSupplier = null;
    }

    /// <summary>获取或设置 DataGrid 分类编辑器当前选择。</summary>
    [ObservableProperty] public partial ProductCategoryCacheNode? EditCategory { get; set; }
    /// <summary>获取或设置 DataGrid 供应商编辑器当前选择。</summary>
    [ObservableProperty] public partial SupplierDto? EditSupplier { get; set; }

    private Task<bool> LoadPageAsync() => LoadPageCoreAsync();

    private async Task<bool> LoadPageCoreAsync()
    {
        var selectedId = SelectedMaster?.Uid;
        var sequence = Interlocked.Increment(ref _querySequence);
        var currentCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _queryCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        IsQuerying = true;
        try
        {
            var requestedCategoryFilter = CategoryFilter;
            var request = new ProductMasterSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                CategoryIds = requestedCategoryFilter.IsAll
                    ? []
                    : requestedCategoryFilter.CategoryIds.Where(id => id != Guid.Empty).Distinct().ToList(),
                IncludeUncategorized = !requestedCategoryFilter.IsAll && requestedCategoryFilter.IncludeUncategorized,
                PageIndex = PageIndex,
                PageSize = PageSize
            };
            var result = await _runner.RunAsync(() => _api.GetPagedAsync(request, currentCts.Token), currentCts.Token, "加载主产品失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _querySequence)) return false;
            if (!result.Success)
            {
                ResultSummary = "主产品加载失败。";
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载主产品失败", result.Message);
                return false;
            }
            var page = result.Data;
            Masters.LoadFromDtos(page?.Items ?? []);
            SelectedMaster = Masters.Items.FirstOrDefault(line => line.Uid == selectedId) ?? Masters.Items.FirstOrDefault();
            ResetNeutralContext(SelectedMaster);
            TotalCount = page?.TotalCount ?? 0;
            PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
            PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 个主型号，本页 {Masters.Items.Count} 个。";
            _appliedCategoryFilter = requestedCategoryFilter;
            OpenImportCommand.NotifyCanExecuteChanged();
            AcceptChanges();
            UpdateChangeState();
            if (!IsCardView) await LoadNeutralsCoreAsync();
            return true;
        }
        finally
        {
            if (ReferenceEquals(_queryCts, currentCts)) IsQuerying = false;
        }
    }

    /// <summary>按筛选语义比较快照，避免只因分类标识集合实例不同而重复查询。</summary>
    private static bool AreCategoryFiltersEquivalent(
        ProductCategoryFilterSelection left,
        ProductCategoryFilterSelection right) =>
        left.IsAll == right.IsAll
        && left.IncludeUncategorized == right.IncludeUncategorized
        && left.CategoryIds.SequenceEqual(right.CategoryIds);

    /// <summary>全页替换或关闭前统一检查两表，保存中禁止关闭，确认取消时不改变上下文。</summary>
    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return false;
        if (!HasUnsavedChanges) return true;
        IsSelectionPrompt = true;
        try
        {
            return await _dialogs.ConfirmAsync("放弃未保存变更", "主型号或中性产品有未保存修改，继续将放弃这些修改。", "继续");
        }
        finally { IsSelectionPrompt = false; }
    }

    private void OnListStateChanged(object? sender, ListStateChangedEventArgs<ProductMasterLineModel> e) => UpdateChangeState();

    private void UpdateChangeState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        OpenDetailCommand.NotifyCanExecuteChanged();
        NotifySyncCommands();
        NotifyNeutralCommands();
    }

    private bool CanSave() => IsNotBusy && CanWrite && Masters.HasChanges;
    /// <summary>仅允许有产品编辑权限且页面两张表均无草稿时下发已保存主型号。</summary>
    private bool CanSync()
    {
        if (!IsInteractionEnabled || HasUnsavedChanges || _currentUser?.HasPermission(PermissionKeys.ProductsProductEdit) != true) return false;
        var lines = Masters.CheckedItems.Count > 0 ? Masters.CheckedItems : SelectedMaster is null ? [] : [SelectedMaster];
        return lines.Count > 0 && lines.All(line => !line.IsNew && !line.IsDeleted && line.Uid != Guid.Empty);
    }

    /// <summary>在选择、草稿、Busy 或权限变化时刷新五个下发入口的可执行状态。</summary>
    private void NotifySyncCommands()
    {
        SyncDescriptionsCommand.NotifyCanExecuteChanged();
        SyncFeaturesCommand.NotifyCanExecuteChanged();
        SyncSupplierCommand.NotifyCanExecuteChanged();
        SyncWeightVolumeCommand.NotifyCanExecuteChanged();
        SyncAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>身份权限快照变化时立即收回或开放产品资料下发入口。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        NotifySyncCommands();
        OpenImportCommand.NotifyCanExecuteChanged();
    }
    private bool CanDeleteSelected() => IsNotBusy && CanWrite && (Masters.CheckedCount > 0 || SelectedMaster is not null);
    private bool CanOpenDetail(ProductMasterLineModel? line = null)
    {
        var target = line ?? SelectedMaster;
        return !_disposed && IsInteractionEnabled && target is not null && !target.IsDeleted && !target.IsNew && target.Uid != Guid.Empty;
    }

    /// <summary>取消查询并解除列表订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WeakReferenceMessenger.Default.Unregister<ProductMasterDetailSavedMessage>(this);
        foreach (var pending in _detailRefreshes.Values) { pending.Cancel(); pending.Dispose(); }
        _detailRefreshes.Clear();
        _imageAddresses.Changed -= OnImageConfigurationChanged;
        Masters.ListStateChanged -= OnListStateChanged;
        Neutrals.ListStateChanged -= OnNeutralListStateChanged;
        PropertyChanged -= OnManagerStateChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        CommitPendingEdits = null;
        CancelNeutralQuery();
        var cts = Interlocked.Exchange(ref _queryCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>打开独立导入队列，文件数据不依赖列表筛选或未保存草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenImport))]
    private Task OpenImportAsync() => _navigation.OpenDocumentAsync(ProductMasterImportViewModel.RouteKey,
        $"product-master-import:{Guid.NewGuid():N}");
    private bool CanOpenImport() => !_disposed && IsNotBusy
        && _currentUser?.HasPermission(PermissionKeys.ProductsMasterView) == true;

    /// <summary>观察详情保存后的定向查询，异步通知不抛未观察异常，不替换管理页的整张编辑集合。</summary>
    private void ObserveDetailSaved(ProductMasterDetailSavedMessage message)
    {
        if (!_disposed && message.ProductMasterId != Guid.Empty) _ = RefreshSavedDetailAsync(message.ProductMasterId);
    }

    /// <summary>每个主型号只接纳最新重读，更新前提交原生缓冲并复核草稿，避免推进草稿的旧版本。</summary>
    private async Task RefreshSavedDetailAsync(Guid masterId)
    {
        CancellationTokenSource? current = null;
        try
        {
            var target = Masters.Items.FirstOrDefault(row => row.Uid == masterId);
            if (target is null) return;
            if (!TryCommitPendingEdits() || target.IsDirty)
            {
                ResultSummary = "主型号详情已保存；对应列表行仍有草稿，请处理草稿后刷新。";
                return;
            }
            current = new CancellationTokenSource();
            if (_detailRefreshes.Remove(masterId, out var previous)) { previous.Cancel(); previous.Dispose(); }
            _detailRefreshes[masterId] = current;
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(masterId, current.Token), current.Token, "刷新主型号摘要失败");
            if (_disposed || current.IsCancellationRequested || !_detailRefreshes.TryGetValue(masterId, out var owner) || !ReferenceEquals(owner, current)) return;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = "详情已保存，但列表行刷新失败，请稍后手动刷新。";
                return;
            }
            if (!Masters.Items.Contains(target) || !TryCommitPendingEdits() || target.IsDirty)
            {
                ResultSummary = "详情已保存；当前列表草稿已保留，请处理后刷新。";
                return;
            }
            var detail = result.Data;
            var snapshot = detail.Adapt<ProductMasterListItemDto>();
            snapshot.TagSummary = string.Join("；", detail.AttributeSelections.GroupBy(row => row.AttributeGroupId)
                .OrderBy(group => group.First().GroupSortOrder).ThenBy(group => group.Key)
                .Select(group => group.First().GroupName + "：" + string.Join("、", group.OrderBy(row => row.OptionSortOrder).ThenBy(row => row.OptionId)
                    .Select(row => row.OptionName + (row.IsAvailable ? "" : "（已失效）")))));
            var main = detail.Neutrals.FirstOrDefault(row => row.Data.IsMainImage)?.Data;
            snapshot.MainImagePath = main?.ImagePath ?? string.Empty;
            snapshot.DefaultSalesPriceUsd = main?.DefaultSalesPriceUsd;
            snapshot.DefaultPurchasePriceCny = main?.DefaultPurchasePriceCny;
            target.Load(snapshot);
            if (_neutralMaster?.Uid == masterId && !Neutrals.HasChanges)
            {
                CancelNeutralQuery();
                AcceptNeutrals(detail);
            }
            UpdateChangeState();
        }
        catch (OperationCanceledException) when (current?.IsCancellationRequested == true) { }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("主型号详情保存后的列表投影接纳失败：{0}", exception);
            if (!_disposed && current?.IsCancellationRequested != true) ResultSummary = "详情已保存；列表刷新未完成，请稍后手动刷新。";
        }
        finally
        {
            if (current is not null && _detailRefreshes.TryGetValue(masterId, out var owner) && ReferenceEquals(owner, current))
            {
                _detailRefreshes.Remove(masterId);
                current.Dispose();
            }
        }
    }
}
