using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Services.Images;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using Mapster;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SwiftExportClient.Features.Products.ProductMasterImport.ViewModels;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>主产品管理页模型，负责主型号筛选、列表编辑、详情导航及中性产品联动。</summary>
public sealed partial class ProductMasterManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>获取主产品管理导航路由键。</summary>
    public const string RouteKey = "product-master-manager";
    private readonly IProductMasterApi _api;
    private readonly IProductNeutralApi _neutralApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService? _currentUser;
    private readonly INavigationService _navigation;
    private readonly IImageAddressResolver _imageAddresses;
    private CancellationTokenSource? _queryCts;
    private long _querySequence;
    private ProductCategoryFilterSelection _appliedCategoryFilter = ProductCategoryFilterSelection.AllCategories;
    private bool _initialized;
    private bool _disposed;
    private readonly Dictionary<Guid, CancellationTokenSource> _detailRefreshes = [];

    /// <summary>初始化主产品管理页依赖。</summary>
    public ProductMasterManagerViewModel(
        IProductMasterApi api,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        IProductNeutralApi neutralApi, IImageAddressResolver imageAddresses,
        ICurrentUserService? currentUser = null)
    {
        _api = api;
        _neutralApi = neutralApi;
        _runner = runner;
        _cache = cache;
        if (cache is not null)
        {
            cache.ProductAttributesChanged += OnAttributeCandidatesChanged;
            if (cache.ProductCategories is INotifyCollectionChanged categories) categories.CollectionChanged += OnCategoryCacheChanged;
        }
        _messages = messages;
        _dialogs = dialogs;
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
        RegisterBusyAwareCommand(CommitSupplierCommand);
        RegisterBusyAwareCommand(ClearSupplierCommand);
    }

    /// <summary>获取主型号编辑集合。</summary>
    public ProductMasterListModel Masters { get; } = new();
    /// <summary>获取基础资料缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache => _cache;
    /// <summary>获取本页直接勾选的分类和本地未分类选项；查询时展开真实分类后代，不修改共享缓存。</summary>
    public ObservableCollection<ProductCategoryCacheNode> FilterCategories { get; } = [];
    /// <summary>获取当前会话启用的属性模板候选，供列表内本地选择。</summary>
    public IReadOnlyList<SysDictDataDto> ProfileCandidates => _cache?.GetDictionaryValues(DictCodes.ProductAttributeProfile) ?? [];
    /// <summary>当前会话启用的系列字典候选，列表编辑使用本地搜索。</summary>
    public IReadOnlyList<SysDictDataDto> SeriesCandidates => _cache?.GetDictionaryValues(DictCodes.ProductSeries) ?? [];
    /// <summary>供应商编辑器使用当前会话完整缓存，本地筛选不发起远程查询。</summary>
    public IReadOnlyList<SupplierDto> SupplierOptions => _cache?.Suppliers ?? (IReadOnlyList<SupplierDto>)[];
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
    /// <summary>获取或设置直接勾选分类的筛选快照；查询失败时恢复最后成功的条件。</summary>
    [ObservableProperty] public partial ProductCategoryFilterSelection CategoryFilter { get; set; } = ProductCategoryFilterSelection.AllCategories;
    /// <summary>获取多选集合是否含本地未分类选项；特殊空身份只转换为请求布尔条件。</summary>
    public bool IncludeUncategorized => FilterCategories.Any(category => category.Uid == Guid.Empty);
    /// <summary>获取或设置当前主型号行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductMasterLineModel? SelectedMaster { get; set; }
    /// <summary>获取或设置是否显示只读卡片视图。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsCardView { get; set; } = false;
    /// <summary>获取或设置是否显示英文描述与英文配置列。</summary>
    [ObservableProperty] public partial bool IsEnglishFieldsVisible { get; set; } = false;
    /// <summary>获取或设置格子选择模式；列表处于此模式时禁止详情编辑和删除，保留单元格编辑及填充。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsCellSelectionMode { get; set; } = false;
    /// <summary>获取当前是否正在查询。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    public partial bool IsQuerying { get; set; }
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

    /// <summary>多选确定后提交直接勾选身份；未勾选分类和未分类时表示全部。</summary>
    [RelayCommand]
    private Task ApplySelectedCategoriesAsync()
    {
        var ids = FilterCategories.Select(category => category.Uid).Where(id => id != Guid.Empty).Distinct().ToArray();
        var selection = ids.Length == 0 && !IncludeUncategorized
            ? ProductCategoryFilterSelection.AllCategories
            : new ProductCategoryFilterSelection(false, IncludeUncategorized, ids);
        return ApplyCategoryFilterAsync(selection);
    }

    /// <summary>筛选快照变化只同步本页编辑器，不在属性回调中发送查询。</summary>
    partial void OnCategoryFilterChanged(ProductCategoryFilterSelection value) => RestoreCategoryFilterEditor(value);

    /// <summary>应用分类筛选快照；取消丢弃或查询失败时恢复最后一次成功的直接勾选。</summary>
    [RelayCommand]
    private async Task ApplyCategoryFilterAsync(ProductCategoryFilterSelection? selection)
    {
        var requestedFilter = selection ?? ProductCategoryFilterSelection.AllCategories;
        if (AreCategoryFiltersEquivalent(requestedFilter, _appliedCategoryFilter))
        {
            CategoryFilter = _appliedCategoryFilter;
            RestoreCategoryFilterEditor(_appliedCategoryFilter);
            return;
        }

        if (!await EnsureCanDiscardChangesAsync())
        {
            CategoryFilter = _appliedCategoryFilter;
            RestoreCategoryFilterEditor(_appliedCategoryFilter);
            return;
        }

        CategoryFilter = requestedFilter;
        PageIndex = 1;
        if (!await LoadPageAsync())
        {
            CategoryFilter = _appliedCategoryFilter;
            RestoreCategoryFilterEditor(_appliedCategoryFilter);
        }
    }

    /// <summary>按身份恢复筛选控件；缓存暂缺时保留已有候选，避免静默丢失勾选。</summary>
    private void RestoreCategoryFilterEditor(ProductCategoryFilterSelection selection)
    {
        var previous = FilterCategories.DistinctBy(category => category.Uid).ToDictionary(category => category.Uid);
        FilterCategories.Clear();
        if (!selection.IsAll)
        {
            foreach (var id in selection.CategoryIds.Where(id => id != Guid.Empty).Distinct())
            {
                var category = _cache?.ProductCategories.FirstOrDefault(item => item.Uid == id)
                    ?? previous.GetValueOrDefault(id);
                if (category is not null) FilterCategories.Add(category);
            }
        }
        if (!selection.IsAll && selection.IncludeUncategorized)
            FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
    }

    /// <summary>按最新分类树展开全部后代并去重；缺失分类保留请求身份，不将其误解为全部。</summary>
    private List<Guid> ExpandCategoryFilter(ProductCategoryFilterSelection selection) => selection.CategoryIds
        .Where(id => id != Guid.Empty)
        .SelectMany(id => _cache?.ProductCategories.FirstOrDefault(category => category.Uid == id) is { } category
            ? GetCategoryIds(category) : [id])
        .Distinct().ToList();

    /// <summary>枚举分类自身及全部后代；父子同时勾选由请求构建统一去重。</summary>
    private static IEnumerable<Guid> GetCategoryIds(ProductCategoryCacheNode category)
    {
        if (category.Uid != Guid.Empty) yield return category.Uid;
        foreach (var child in category.Children)
            foreach (var id in GetCategoryIds(child)) yield return id;
    }

    /// <summary>关闭页面前检查未保存变更。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => EnsureCanDiscardChangesAsync();

    partial void OnSelectedMasterChanged(ProductMasterLineModel? value)
    {
        Masters.SelectedItem = value;
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

    /// <summary>整行或卡片模式下删除目标主型号；已保存行先确认，完成后选择剩余最后一行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!IsInteractionEnabled || !CanDeleteSelected() || !TryCommitPendingEdits()) return;
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

    /// <summary>提交原生表格编辑并校验后保存主型号列表，不覆盖关联物料资料。</summary>
    /// <remarks>保存失败保留当前编辑；成功后回填行版本，下表有未保存草稿时跳过列表刷新。</remarks>
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

            // 请求只包含本页主型号的新增、修改和删除，不附带物料覆盖选项。
            var request = Masters.CreateSaveRequest();

            if (!request.HasChanges) return;

            // Refit POST /api/ProductMaster/batch-save，仅保存主型号本身的变更。
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

    /// <summary>仅在确认候选分类时同步行标识和显示名称，不将搜索词作为业务数据。</summary>
    [RelayCommand]
    private void CommitCategory(ProductMasterLineModel? line)
    {
        if (line is null || EditCategory is null) return;
        line.CategoryId = EditCategory.Uid;
        line.CategoryName = GetCategoryDisplayName(EditCategory);
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

    /// <summary>仅确认字典候选时修改当前行模板，引用失效由后端拒绝并引导详情处理。</summary>
    [RelayCommand]
    private void CommitProfile(ProductMasterLineModel? line)
    {
        if (line is null || EditProfile is null || !CanWrite || IsBusy) return;
        line.ProductAttributeProfileId = EditProfile.Uid;
        line.ProductAttributeProfileName = EditProfile.DataNameZh ?? EditProfile.DataNameEn ?? string.Empty;
        EditProfile = null;
    }

    /// <summary>明确清空可选模板，保存时不自动清空已有标签选择。</summary>
    [RelayCommand]
    private void ClearProfile(ProductMasterLineModel? line)
    {
        if (line is null || !CanWrite || IsBusy) return;
        line.ProductAttributeProfileId = null;
        line.ProductAttributeProfileName = string.Empty;
        EditProfile = null;
    }

    /// <summary>获取或设置列表模板编辑器的临时候选，不保存控件搜索文字。</summary>
    [ObservableProperty] public partial SysDictDataDto? EditProfile { get; set; }

    /// <summary>获取或设置系列编辑器的临时候选，输入文本不参与持久化。</summary>
    [ObservableProperty] public partial SysDictDataDto? EditSeries { get; set; }

    /// <summary>确认有效系列候选后只修改目标行，其他行草稿保持不变。</summary>
    [RelayCommand]
    private void CommitSeries(ProductMasterLineModel? line)
    {
        if (line is null || EditSeries is null || !CanWrite || IsBusy
            || !SeriesCandidates.Any(item => item.Uid == EditSeries.Uid)) return;
        line.ProductSeriesId = EditSeries.Uid;
        line.ProductSeriesName = EditSeries.DataNameZh ?? EditSeries.DataNameEn ?? string.Empty;
        EditSeries = null;
    }

    /// <summary>明确清空可选系列身份和展示名称，不自动提交列表。</summary>
    [RelayCommand]
    private void ClearSeries(ProductMasterLineModel? line)
    {
        if (line is null || !CanWrite || IsBusy) return;
        line.ProductSeriesId = null;
        line.ProductSeriesName = string.Empty;
        EditSeries = null;
    }

    /// <summary>字典或标签快照变化只更新候选，不接纳列表草稿。</summary>
    private void OnAttributeCandidatesChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(ProfileCandidates));
        OnPropertyChanged(nameof(SeriesCandidates));
        // 显示资料随字典更新，身份和当前列表的编辑基线不变。
        foreach (var line in Masters.Items)
            if (line.ProductSeriesId is { } id && SeriesCandidates.FirstOrDefault(item => item.Uid == id) is { } series)
                line.ProductSeriesName = series.DataNameZh ?? series.DataNameEn ?? string.Empty;
    }

    /// <summary>只在明确确认供应商候选后修改目标行，不将搜索文字保存为供应商名称。</summary>
    [RelayCommand]
    private void CommitSupplier(ProductMasterLineModel? line)
    {
        if (_disposed || line is null || EditSupplier is null || !CanWrite || IsBusy || IsQuerying) return;
        line.DefaultSupplierId = EditSupplier.Uid;
        line.DefaultSupplierName = EditSupplier.SupplierName ?? string.Empty;
        // 双向候选绑定保留已确认项，当前单元格才能继续显示选择并执行显式清空。
    }

    /// <summary>明确清空可选供应商，只清除目标行关系及临时候选，保留其他行草稿。</summary>
    [RelayCommand]
    private void ClearSupplier(ProductMasterLineModel? line)
    {
        if (_disposed || line is null || !CanWrite || IsBusy || IsQuerying) return;
        line.DefaultSupplierId = Guid.Empty;
        line.DefaultSupplierName = string.Empty;
        EditSupplier = null;
    }

    /// <summary>获取或设置 DataGrid 分类编辑器当前选择。</summary>
    [ObservableProperty] public partial ProductCategoryCacheNode? EditCategory { get; set; }

    /// <summary>分类列优先展示中文 FakeName；未维护中文名时保留已有分类名称。</summary>
    private static string GetCategoryDisplayName(ProductCategoryCacheNode category) =>
        string.IsNullOrWhiteSpace(category.FakeName) ? category.CategoryName : category.FakeName;

    /// <summary>只更新分类显示资料；加载完成后把中文名称接纳为当前行的展示基线。</summary>
    private void RefreshCategoryDisplayName(ProductMasterLineModel line)
    {
        if (_cache?.ProductCategories.FirstOrDefault(category => category.Uid == line.CategoryId) is not { } category) return;
        using (line.SuppressChangeTracking()) line.CategoryName = GetCategoryDisplayName(category);
    }

    /// <summary>分类缓存更新只重匹配无草稿行的中文名称，不能改写正在编辑行的分类或回滚基线。</summary>
    private void OnCategoryCacheChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        foreach (var line in Masters.Items.Where(line => !line.IsDirty)) RefreshCategoryDisplayName(line);
    }
    /// <summary>获取或设置 DataGrid 供应商编辑器当前选择。</summary>
    [ObservableProperty] public partial SupplierDto? EditSupplier { get; set; }

    /// <summary>原生单元格进入编辑时按当前行身份恢复缓存候选，不修改行资料或接纳草稿。</summary>
    public void PrepareSupplierEditor(ProductMasterLineModel? line)
    {
        if (_disposed) return;
        EditSupplier = line is null || line.DefaultSupplierId == Guid.Empty ? null
            : SupplierOptions.FirstOrDefault(item => item.Uid == line.DefaultSupplierId)
              ?? new SupplierDto { Uid = line.DefaultSupplierId, SupplierName = line.DefaultSupplierName };
    }

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
                    : ExpandCategoryFilter(requestedCategoryFilter),
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
            foreach (var line in Masters.Items) RefreshCategoryDisplayName(line);
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
        NotifyNeutralCommands();
    }

    private bool CanSave() => IsNotBusy && CanWrite && Masters.HasChanges;
    /// <summary>身份权限快照变化时刷新主型号导入入口。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        OpenImportCommand.NotifyCanExecuteChanged();
    }
    /// <summary>列表格子模式不提供整行删除；卡片模式沿用其当前项和勾选项。</summary>
    private bool CanDeleteSelected() => (IsCardView || !IsCellSelectionMode)
        && IsNotBusy && CanWrite && (Masters.CheckedCount > 0 || SelectedMaster is not null);
    /// <summary>只有整行或卡片模式下的已保存主型号可以打开详情。</summary>
    private bool CanOpenDetail(ProductMasterLineModel? line = null)
    {
        var target = line ?? SelectedMaster;
        return (IsCardView || !IsCellSelectionMode) && !_disposed && IsInteractionEnabled
            && target is not null && !target.IsDeleted && !target.IsNew && target.Uid != Guid.Empty;
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
        if (_cache is not null) _cache.ProductAttributesChanged -= OnAttributeCandidatesChanged;
        if (_cache?.ProductCategories is INotifyCollectionChanged categories) categories.CollectionChanged -= OnCategoryCacheChanged;
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
            RefreshCategoryDisplayName(target);
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
