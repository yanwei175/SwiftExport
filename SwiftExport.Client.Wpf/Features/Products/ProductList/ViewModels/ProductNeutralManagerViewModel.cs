using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Common;
using SwiftExportClient.Services.Images;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>中性产品管理页模型，编排分类和工具栏颜色筛选、跨主型号编辑、分页与原子批量保存。</summary>
public sealed partial class ProductNeutralManagerViewModel
    : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>获取中性产品管理导航路由键。</summary>
    public const string RouteKey = "product-neutral-manager";

    private readonly IProductNeutralManagerApi _api;
    private readonly IProductMasterApi _productMasterApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IImageAddressResolver _imageAddresses;
    private readonly INavigationService? _navigation;
    private readonly ICurrentUserService? _currentUser;
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _masterLookupCts;
    private long _querySequence;
    private long _masterLookupSequence;
    private ProductCategoryFilterSelection _appliedCategoryFilter = ProductCategoryFilterSelection.AllCategories;
    private IReadOnlyList<ProductCategoryCacheNode> _appliedSelectedCategories = [];
    private IReadOnlyList<ProductColorDto> _appliedColorFilter = [];
    private bool _initialized;
    private bool _disposed;

    /// <summary>初始化页面依赖并连接集合状态通知。</summary>
    public ProductNeutralManagerViewModel(
        IProductNeutralManagerApi api,
        IProductMasterApi productMasterApi,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        IImageAddressResolver imageAddresses,
        INavigationService? navigation = null,
        ICurrentUserService? currentUser = null)
    {
        _api = api;
        _productMasterApi = productMasterApi;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _imageAddresses = imageAddresses;
        _navigation = navigation;
        _currentUser = currentUser;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnImportUserChanged;
        _imageAddresses.Changed += OnImageConfigurationChanged;
        Title = "产品管理";
        Lines.ListStateChanged += OnListStateChanged;
        PropertyChanged += OnManagerPropertyChanged;
        InitializeCheckSelection();
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ReBuildImagePathCommand);
        RegisterBusyAwareCommand(ReBuildCheckedImagePathsCommand);
        RegisterBusyAwareCommand(OpenImportCommand);
    }

    /// <summary>由宿主视图提交原生 DataGrid 编辑事务。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>获取中性产品编辑集合。</summary>
    public ProductNeutralManagerListModel Lines { get; } = new();
    /// <summary>获取共享基础资料缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache => _cache;
    /// <summary>获取主型号远程搜索候选。</summary>
    public ObservableCollection<ProductMasterLookupItemModel> ProductMasterCandidates { get; } = [];
    /// <summary>获取文档页签标题。</summary>
    public string DocumentTitle => Title;
    /// <summary>获取页面是否存在未保存编辑。</summary>
    public bool HasUnsavedChanges => Lines.HasChanges;
    /// <summary>获取页面是否允许交互。</summary>
    public bool IsInteractionEnabled => !_disposed && IsNotBusy && !IsQuerying && !IsSelectionPrompt;
    /// <summary>获取分页总页数。</summary>
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    /// <summary>获取分页状态文本。</summary>
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    /// <summary>获取或设置搜索关键词。</summary>
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    /// <summary>获取或设置已展开后代的分类查询快照；顶部控件只保存独立勾选的分类对象。</summary>
    [ObservableProperty] public partial ProductCategoryFilterSelection CategoryFilter { get; set; } = ProductCategoryFilterSelection.AllCategories;
    /// <summary>获取顶部多选确定的分类和本地未分类选项；空集合表示全部，不修改共享缓存。</summary>
    public ObservableCollection<ProductCategoryCacheNode> FilterCategories { get; } = [];
    /// <summary>获取多选集合是否含本地未分类选项；单独勾选表示仅查询未分类。</summary>
    public bool IncludeUncategorized => FilterCategories.Any(category => category.Uid == Guid.Empty);
    /// <summary>获取工具栏颜色多选筛选的已提交对象集合；空集合表示全部颜色。</summary>
    public ObservableCollection<ProductColorDto> FilterColorList { get; } = [];

    /// <summary>获取或设置当前中性产品行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductNeutralManagerLineModel? SelectedLine { get; set; }
    /// <summary>只影响本页的原生选择语义；格子模式不提供整行选区勾选或删除。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial bool IsCellSelectionMode { get; set; }
    /// <summary>获取或设置 DataGrid 主型号编辑候选。</summary>
    [ObservableProperty] public partial ProductMasterLookupItemModel? EditProductMaster { get; set; }
    /// <summary>获取或设置 DataGrid 颜色编辑候选。</summary>
    [ObservableProperty] public partial ProductColorDto? EditColor { get; set; }
    /// <summary>获取主型号远程搜索等待状态。</summary>
    [ObservableProperty] public partial bool IsProductMasterLookupBusy { get; set; }
    /// <summary>获取分页查询等待状态。</summary>
    [ObservableProperty] public partial bool IsQuerying { get; set; }
    /// <summary>获取筛选或丢弃确认状态。</summary>
    [ObservableProperty] public partial bool IsSelectionPrompt { get; set; }
    /// <summary>获取当前查询结果摘要。</summary>
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载中性产品。";
    /// <summary>获取或设置分页总记录数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }
    /// <summary>获取或设置当前页码。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;
    /// <summary>获取或设置每页记录数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = 20;

    partial void OnSelectedLineChanged(ProductNeutralManagerLineModel? value) => Lines.SelectedItem = value;

    /// <summary>幂等加载基础资料和第一页中性产品。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var cacheResult = await _cache.EnsureLoadedAsync();
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        await LoadPageAsync();
    }

    /// <summary>展开已勾选分类及其后代，再应用查询；取消或失败时恢复控件上一次成功应用的选择。</summary>
    [RelayCommand]
    private async Task ApplySelectedCategoriesAsync()
    {
        var selected = FilterCategories.DistinctBy(category => category.Uid).ToArray();
        var ids = selected.Where(category => category.Uid != Guid.Empty).SelectMany(category => GetCategoryIds(
            _cache.ProductCategories.FirstOrDefault(current => current.Uid == category.Uid) ?? category)).Distinct().ToArray();
        var requested = selected.Length == 0
            ? ProductCategoryFilterSelection.AllCategories : new(false, IncludeUncategorized, ids);
        await ApplyCategoryFilterAsync(requested);
        if (_disposed) return;
        if (AreCategoryFiltersEquivalent(requested, _appliedCategoryFilter))
            _appliedSelectedCategories = selected;
        else
        {
            FilterCategories.Clear();
            foreach (var category in _appliedSelectedCategories) FilterCategories.Add(category);
        }
    }

    /// <summary>枚举分类及全部后代身份；父子同时勾选由查询快照统一去重。</summary>
    private static IEnumerable<Guid> GetCategoryIds(ProductCategoryCacheNode category)
    {
        if (category.Uid != Guid.Empty) yield return category.Uid;
        foreach (var child in category.Children)
            foreach (var id in GetCategoryIds(child)) yield return id;
    }

    /// <summary>应用分类查询快照；取消丢弃或查询失败时恢复最后一次成功应用的条件。</summary>
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
        if (await LoadPageCoreAsync() == PageLoadOutcome.Failed) CategoryFilter = _appliedCategoryFilter;
    }

    /// <summary>关闭文档前检查原生编辑提交和未保存变更。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => EnsureCanDiscardChangesAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!await EnsureCanDiscardChangesAsync())
        {
            RestoreAppliedColorFilter();
            return;
        }
        var requestedColorFilter = FilterColorList
            .Where(color => color.Uid != Guid.Empty)
            .DistinctBy(color => color.Uid)
            .ToArray();
        PageIndex = 1;
        if (await LoadPageCoreAsync(requestedColorFilter) == PageLoadOutcome.Failed) RestoreAppliedColorFilter();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private void AddRow()
    {
        if (!TryCommitPendingEdits()) return;
        SelectedLine = Lines.AddNewNeutral();
        UpdateChangeState();
    }

    /// <summary>新增行直接移除；持久化行确认后进入软删除池，最终定位剩余最后一行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDeleteSelected() || !TryCommitPendingEdits()) return;
        var targets = Lines.CheckedItems.Count > 0
            ? Lines.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (targets.Length == 0) return;
        if (targets.Any(line => !line.IsNew))
        {
            IsSelectionPrompt = true;
            try
            {
                if (!await _dialogs.ConfirmAsync("删除中性产品", $"确定删除选中的 {targets.Length} 条中性产品吗？保存后生效。", "删除")) return;
            }
            finally { IsSelectionPrompt = false; }
        }
        if (!CanDeleteSelected()) return;
        var last = Lines.Items.LastOrDefault(line => !targets.Contains(line));
        foreach (var target in targets) Lines.Remove(target);
        SelectedLine = last;
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!TryCommitPendingEdits()) return;
        await RunWriteAsync(async () =>
        {
            var error = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("中性产品未保存", error);
                return;
            }
            var request = Lines.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存中性产品失败");
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                _messages.ShowError("保存中性产品失败", result.Message);
                return;
            }
            Lines.ApplySaveResponse(result.Data);
            RefreshCategoryDisplayNames();
            UpdateChangeState();
            _messages.ShowSuccess("中性产品已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            if (await LoadPageCoreAsync() == PageLoadOutcome.Failed)
                _messages.ShowWarning("保存成功", "数据已保存，但列表刷新失败。请稍后手动刷新。");
        }, "正在保存中性产品...");
    }

    [RelayCommand]
    private void RejectListChanges()
    {
        if (!IsInteractionEnabled || !TryCommitPendingEdits()) return;
        Lines.RejectChanges();
        SelectedLine = Lines.Items.LastOrDefault();
        UpdateChangeState();
    }

    [RelayCommand]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await EnsureCanDiscardChangesAsync()) return;
        PageIndex = Math.Max(1, request.NewIndex);
        PageSize = request.NewSize <= 0 ? 20 : request.NewSize;
        await LoadPageAsync();
    }

    /// <summary>远程搜索主型号候选，取消旧请求并阻止过期结果回写。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductMastersAsync(string? keyword)
    {
        var sequence = Interlocked.Increment(ref _masterLookupSequence);
        var currentCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _masterLookupCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        IsProductMasterLookupBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _productMasterApi.GetPagedAsync(new ProductMasterSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
                PageIndex = 1,
                PageSize = 50
            }, currentCts.Token), currentCts.Token, "搜索主型号失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _masterLookupSequence)) return;
            ProductMasterCandidates.Clear();
            foreach (var item in result.Success ? result.Data?.Items ?? [] : [])
                ProductMasterCandidates.Add(new ProductMasterLookupItemModel(item));
        }
        finally
        {
            if (ReferenceEquals(_masterLookupCts, currentCts)) IsProductMasterLookupBusy = false;
        }
    }

    /// <summary>确认主型号候选后回填稳定标识和实时显示字段。</summary>
    [RelayCommand]
    private void CommitProductMaster(ProductNeutralManagerLineModel? line)
    {
        if (line is null || EditProductMaster is null) return;
        line.ProductMasterId = EditProductMaster.Uid;
        line.MasterSku = EditProductMaster.MasterSku ?? string.Empty;
        line.ProductMasterDescriptionZh = EditProductMaster.DefaultDescriptionZh ?? string.Empty;
        line.CategoryId = EditProductMaster.CategoryId;
        line.CategoryName = GetCategoryDisplayName(line.CategoryId, EditProductMaster.CategoryName ?? string.Empty);
        EditProductMaster = null;
    }

    /// <summary>显式清空当前行主型号关系及其派生显示字段。</summary>
    [RelayCommand]
    private void ClearProductMaster(ProductNeutralManagerLineModel? line)
    {
        if (line is null) return;
        line.ProductMasterId = Guid.Empty;
        line.MasterSku = string.Empty;
        line.ProductMasterDescriptionZh = string.Empty;
        line.CategoryId = Guid.Empty;
        line.CategoryName = string.Empty;
        EditProductMaster = null;
    }

    /// <summary>确认颜色候选后回填颜色标识和显示名称。</summary>
    [RelayCommand]
    private void CommitColor(ProductNeutralManagerLineModel? line)
    {
        if (line is null || EditColor is null) return;
        line.ColorId = EditColor.Uid;
        line.ColorName = string.IsNullOrWhiteSpace(EditColor.ColorNameZh) ? EditColor.ColorNameEn ?? string.Empty : EditColor.ColorNameZh;
        EditColor = null;
    }

    /// <summary>显式清空当前行颜色。</summary>
    [RelayCommand]
    private void ClearColor(ProductNeutralManagerLineModel? line)
    {
        if (line is null) return;
        line.ColorId = Guid.Empty;
        line.ColorName = string.Empty;
        EditColor = null;
    }

    /// <summary>按最新重建基址与完整编码生成网络地址或本地路径，变更由页面保存命令统一提交。</summary>
    [RelayCommand(CanExecute = nameof(CanReBuildImagePath))]
    private void ReBuildImagePath(ProductNeutralManagerLineModel? line)
    {
        if (!CanReBuildImagePath(line)) return;
        if (_imageAddresses.TryGetProductBaseAddress(out var baseAddress)) ApplyImagePath(line!, baseAddress);
    }

    /// <summary>确认后以同一个基址快照重建勾选行图片引用；空或不安全编码保持原值并计入跳过数量。</summary>
    [RelayCommand(CanExecute = nameof(CanReBuildCheckedImagePaths))]
    private async Task ReBuildCheckedImagePathsAsync()
    {
        if (!CanReBuildCheckedImagePaths() || !TryCommitPendingEdits()) return;

        var checkedLines = Lines.CheckedItems.ToArray();
        IsSelectionPrompt = true;
        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "批量重建图片路径",
                $"确定重建已勾选的 {checkedLines.Length} 条中性产品图片路径吗？重建后需要保存才会生效。",
                "重建");
            if (!confirmed || _disposed) return;
        }
        finally
        {
            IsSelectionPrompt = false;
        }

        if (!_imageAddresses.TryGetProductBaseAddress(out var baseAddress)) return;
        var rebuiltCount = 0;
        foreach (var line in checkedLines)
        {
            if (ApplyImagePath(line, baseAddress)) rebuiltCount++;
        }

        var skippedCount = checkedLines.Length - rebuiltCount;
        var skippedMessage = skippedCount == 0 ? string.Empty : $"，另有 {skippedCount} 条因产品编码为空或不能作为安全文件名已跳过";
        _messages.ShowSuccess("图片路径已重建", $"已重建 {rebuiltCount} 条中性产品图片路径{skippedMessage}，请保存变更。");
    }

    /// <summary>按固定基址快照生成网络或本地 JPG 引用，保留编码内点号；网络引用才转义文件名。</summary>
    private static bool ApplyImagePath(ProductNeutralManagerLineModel line, string baseAddress)
    {
        if (!ProductImageAddress.TryBuildProductImageReference(baseAddress, line.ProductCode, out var reference)) return false;
        line.ImagePath = reference;
        return true;
    }

    /// <summary>参数保存或刷新后立即更新重建命令资格，无需重启页面。</summary>
    private void OnImageConfigurationChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        ReBuildImagePathCommand.NotifyCanExecuteChanged();
        ReBuildCheckedImagePathsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>打开独立导入队列；固定路由身份复用已有页签，不读取或替换管理页草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenImport))]
    private Task OpenImportAsync() => _navigation!.OpenDocumentAsync(ProductNeutralImportViewModel.RouteKey, ProductNeutralImportViewModel.RouteKey);
    private bool CanOpenImport() => !_disposed && IsNotBusy && _navigation is not null
        && _currentUser?.HasPermission(PermissionKeys.ProductsNeutralView) == true;
    private void OnImportUserChanged(object? sender, EventArgs e) => OpenImportCommand.NotifyCanExecuteChanged();

    private Task<PageLoadOutcome> LoadPageAsync() => LoadPageCoreAsync();

    /// <summary>执行 latest-request-wins 分页查询，仅最新且页面仍存活的结果可以替换集合。</summary>
    private async Task<PageLoadOutcome> LoadPageCoreAsync(IReadOnlyList<ProductColorDto>? requestedColorFilter = null)
    {
        var colors = requestedColorFilter ?? _appliedColorFilter;
        var selectedId = SelectedLine?.Uid;
        var sequence = Interlocked.Increment(ref _querySequence);
        var currentCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _queryCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        IsQuerying = true;
        try
        {
            var requestedCategoryFilter = CategoryFilter;
            var request = new ProductNeutralManagerSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                CategoryIds = requestedCategoryFilter.IsAll
                    ? []
                    : requestedCategoryFilter.CategoryIds.Where(id => id != Guid.Empty).Distinct().ToList(),
                IncludeUncategorized = !requestedCategoryFilter.IsAll && requestedCategoryFilter.IncludeUncategorized,
                ColorIds = colors.Select(color => color.Uid).ToList(),
                PageIndex = PageIndex,
                PageSize = PageSize
            };
            var result = await _runner.RunAsync(() => _api.GetPagedAsync(request, currentCts.Token), currentCts.Token, "加载中性产品失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _querySequence))
                return PageLoadOutcome.Superseded;
            if (!result.Success)
            {
                ResultSummary = "中性产品加载失败。";
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载中性产品失败", result.Message);
                return PageLoadOutcome.Failed;
            }
            var page = result.Data;
            Lines.LoadFromDtos(page?.Items ?? []);
            RefreshCategoryDisplayNames();
            SelectedLine = Lines.Items.FirstOrDefault(line => line.Uid == selectedId) ?? Lines.Items.FirstOrDefault();
            TotalCount = page?.TotalCount ?? 0;
            PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
            PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 条中性产品，本页 {Lines.Items.Count} 条。";
            _appliedCategoryFilter = requestedCategoryFilter;
            _appliedColorFilter = colors.ToArray();
            AcceptChanges();
            UpdateChangeState();
            return PageLoadOutcome.Success;
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

    /// <summary>按稳定分类身份优先读取中文 FakeName；缓存缺失或未维护中文名时保留已有名称。</summary>
    private string GetCategoryDisplayName(Guid categoryId, string fallback)
    {
        var category = _cache?.ProductCategories.FirstOrDefault(item => item.Uid == categoryId);
        return string.IsNullOrWhiteSpace(category?.FakeName) ? fallback : category.FakeName;
    }

    /// <summary>载入或保存回填后接纳中文分类展示基线；不改变关联身份或其他未保存字段。</summary>
    private void RefreshCategoryDisplayNames()
    {
        foreach (var line in Lines.Items)
            using (line.SuppressChangeTracking())
                line.CategoryName = GetCategoryDisplayName(line.CategoryId, line.CategoryName);
    }

    /// <summary>把工具栏颜色选择恢复为最后一次成功查询的快照，避免取消或失败后显示未应用条件。</summary>
    private void RestoreAppliedColorFilter()
    {
        FilterColorList.Clear();
        foreach (var color in _appliedColorFilter) FilterColorList.Add(color);
    }

    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        // 替代型查询允许新请求取消旧请求；写入或确认期间仍禁止切换上下文。
        if (_disposed || IsBusy || IsSelectionPrompt || !TryCommitPendingEdits()) return false;
        if (!HasUnsavedChanges) return true;
        IsSelectionPrompt = true;
        try
        {
            return await _dialogs.ConfirmAsync("放弃未保存变更", "中性产品有未保存修改，继续将放弃这些修改。", "继续");
        }
        finally { IsSelectionPrompt = false; }
    }

    private bool TryCommitPendingEdits() => CommitPendingEdits?.Invoke() ?? true;

    private void OnListStateChanged(object? sender, ListStateChangedEventArgs<ProductNeutralManagerLineModel> e) => UpdateChangeState();

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsQuerying) or nameof(IsSelectionPrompt) or nameof(CanWrite))
        {
            OnPropertyChanged(nameof(IsInteractionEnabled));
            AddRowCommand.NotifyCanExecuteChanged();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            ReBuildImagePathCommand.NotifyCanExecuteChanged();
            ReBuildCheckedImagePathsCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>同步草稿、显示行勾选和保存资格；UI 勾选本身不标记业务修改。</summary>
    private void UpdateChangeState()
    {
        RefreshCheckState();
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ReBuildImagePathCommand.NotifyCanExecuteChanged();
        ReBuildCheckedImagePathsCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddRow() => IsInteractionEnabled && CanWrite;
    /// <summary>仅整行模式允许既有的勾选优先删除，保留交互及写入权限约束。</summary>
    private bool CanDeleteSelected() => !IsCellSelectionMode && IsInteractionEnabled && CanWrite && (Lines.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsInteractionEnabled && CanWrite && Lines.HasChanges;
    private bool CanReBuildImagePath(ProductNeutralManagerLineModel? line) =>
        IsInteractionEnabled
        && CanWrite
        && line is not null
        && ProductImageAddress.TryGetProductFileName(line.ProductCode, out _)
        && _imageAddresses.TryGetProductBaseAddress(out _);
    private bool CanReBuildCheckedImagePaths() =>
        IsInteractionEnabled
        && CanWrite
        && Lines.CheckedItems.Any(line => ProductImageAddress.TryGetProductFileName(line.ProductCode, out _))
        && _imageAddresses.TryGetProductBaseAddress(out _);

    /// <summary>取消分页和主型号搜索请求并解除集合及页面状态订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachCheckSelection();
        if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnImportUserChanged;
        OpenImportCommand.NotifyCanExecuteChanged();
        _imageAddresses.Changed -= OnImageConfigurationChanged;
        Lines.ListStateChanged -= OnListStateChanged;
        PropertyChanged -= OnManagerPropertyChanged;
        CommitPendingEdits = null;
        CancelAndDispose(ref _queryCts);
        CancelAndDispose(ref _masterLookupCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var cts = Interlocked.Exchange(ref source, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>区分分页查询成功、真实失败和被更新请求替代，避免旧请求回滚新筛选。</summary>
    private enum PageLoadOutcome
    {
        Success,
        Failed,
        Superseded,
    }
}
