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
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _masterLookupCts;
    private long _querySequence;
    private long _masterLookupSequence;
    private ProductCategoryFilterSelection _appliedCategoryFilter = ProductCategoryFilterSelection.AllCategories;
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
        IImageAddressResolver imageAddresses)
    {
        _api = api;
        _productMasterApi = productMasterApi;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _imageAddresses = imageAddresses;
        _imageAddresses.Changed += OnImageConfigurationChanged;
        Title = "产品管理";
        Lines.ListStateChanged += OnListStateChanged;
        PropertyChanged += OnManagerPropertyChanged;
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(ReBuildImagePathCommand);
        RegisterBusyAwareCommand(ReBuildCheckedImagePathsCommand);
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
    /// <summary>获取或设置共享分类筛选组件发布的选择快照。</summary>
    [ObservableProperty] public partial ProductCategoryFilterSelection CategoryFilter { get; set; } = ProductCategoryFilterSelection.AllCategories;
    /// <summary>获取工具栏颜色多选筛选的已提交对象集合；空集合表示全部颜色。</summary>
    public ObservableCollection<ProductColorDto> FilterColorList { get; } = [];

    /// <summary>获取或设置当前中性产品行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductNeutralManagerLineModel? SelectedLine { get; set; }
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
        if (!TryCommitPendingEdits()) return;
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
        if (_disposed) return;
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
        line.CategoryName = EditProductMaster.CategoryName ?? string.Empty;
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

    /// <summary>按最新缓存基址与完整产品编码生成原图 URL，变更由页面保存命令统一提交。</summary>
    [RelayCommand(CanExecute = nameof(CanReBuildImagePath))]
    private void ReBuildImagePath(ProductNeutralManagerLineModel? line)
    {
        if (!CanReBuildImagePath(line)) return;
        if (_imageAddresses.TryGetProductBaseUri(out var baseUri)) ApplyImagePath(line!, baseUri);
    }

    /// <summary>确认后以同一个基址快照重建勾选行 URL；空或不安全编码保持原值并计入跳过数量。</summary>
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

        if (!_imageAddresses.TryGetProductBaseUri(out var baseUri)) return;
        var rebuiltCount = 0;
        foreach (var line in checkedLines)
        {
            if (ApplyImagePath(line, baseUri)) rebuiltCount++;
        }

        var skippedCount = checkedLines.Length - rebuiltCount;
        var skippedMessage = skippedCount == 0 ? string.Empty : $"，另有 {skippedCount} 条因产品编码为空或不能作为安全文件名已跳过";
        _messages.ShowSuccess("图片路径已重建", $"已重建 {rebuiltCount} 条中性产品图片路径{skippedMessage}，请保存变更。");
    }

    /// <summary>使用固定基址快照和完整编码生成 JPG 地址，保留编码内点号并转义文件名。</summary>
    private static bool ApplyImagePath(ProductNeutralManagerLineModel line, Uri baseUri)
    {
        if (!ProductImageAddress.TryGetProductFileName(line.ProductCode, out var fileName)) return false;
        line.ImagePath = ProductImageAddress.BuildUrl(baseUri, fileName);
        return true;
    }

    /// <summary>参数保存或刷新后立即更新重建命令资格，无需重启页面。</summary>
    private void OnImageConfigurationChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        ReBuildImagePathCommand.NotifyCanExecuteChanged();
        ReBuildCheckedImagePathsCommand.NotifyCanExecuteChanged();
    }

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

    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ReBuildImagePathCommand.NotifyCanExecuteChanged();
        ReBuildCheckedImagePathsCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddRow() => IsInteractionEnabled && CanWrite;
    private bool CanDeleteSelected() => IsInteractionEnabled && CanWrite && (Lines.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsInteractionEnabled && CanWrite && Lines.HasChanges;
    private bool CanReBuildImagePath(ProductNeutralManagerLineModel? line) =>
        IsInteractionEnabled
        && CanWrite
        && line is not null
        && ProductImageAddress.TryGetProductFileName(line.ProductCode, out _)
        && _imageAddresses.TryGetProductBaseUri(out _);
    private bool CanReBuildCheckedImagePaths() =>
        IsInteractionEnabled
        && CanWrite
        && Lines.CheckedItems.Any(line => ProductImageAddress.TryGetProductFileName(line.ProductCode, out _))
        && _imageAddresses.TryGetProductBaseUri(out _);

    /// <summary>取消分页和主型号搜索请求并解除集合及页面状态订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
