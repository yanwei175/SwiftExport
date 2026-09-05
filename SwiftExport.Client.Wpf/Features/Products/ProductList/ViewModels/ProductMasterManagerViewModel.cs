using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>主产品管理页模型，负责主型号筛选、列表编辑、卡片展示和详情导航。</summary>
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
    private readonly INavigationService _navigation;
    private readonly IOptions<ProductOptions> _productOptions;
    private CancellationTokenSource? _queryCts;
    private long _querySequence;
    private long _supplierSequence;
    private bool _initialized;
    private bool _disposed;

    /// <summary>初始化主产品管理页依赖。</summary>
    public ProductMasterManagerViewModel(
        IProductMasterApi api,
        ISupplierApi supplierApi,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        IProductNeutralApi neutralApi, IOptions<ProductOptions> productOptions,
        IProductMasterSyncDialogService syncDialog)
    {
        _api = api;
        _neutralApi = neutralApi;
        _supplierApi = supplierApi;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _syncDialog = syncDialog;
        _navigation = navigation;
        _productOptions = productOptions;
        Title = "主产品管理";
        Masters.ListStateChanged += OnListStateChanged;
        Neutrals.ListStateChanged += OnNeutralListStateChanged;
        PropertyChanged += OnManagerStateChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(OpenDetailCommand);
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
    /// <summary>获取分类筛选树。</summary>
    [ObservableProperty] public partial IReadOnlyList<ProductMasterCategoryFilterNode> CategoryFilters { get; set; } = [];
    /// <summary>获取或设置当前分类筛选节点。</summary>
    [ObservableProperty] public partial ProductMasterCategoryFilterNode? SelectedCategoryFilter { get; set; }
    /// <summary>获取或设置当前主型号行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductMasterLineModel? SelectedMaster { get; set; }
    /// <summary>获取或设置是否显示只读卡片视图。</summary>
    [ObservableProperty] public partial bool IsCardView { get; set; } = true;
    /// <summary>获取或设置是否显示英文描述与英文配置列。</summary>
    [ObservableProperty] public partial bool IsEnglishFieldsVisible { get; set; } = false;
    /// <summary>获取或设置 DataGrid 是否使用格子选择模式。</summary>
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; } = false;
    /// <summary>获取当前是否正在查询。</summary>
    [ObservableProperty] public partial bool IsQuerying { get; set; }
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
        BuildCategoryFilters();
        await LoadPageAsync();
    }

    /// <summary>从 TreeView 接纳分类选择；有未保存数据时先确认。</summary>
    public async Task SelectCategoryAsync(ProductMasterCategoryFilterNode? node)
    {
        if (ReferenceEquals(node, SelectedCategoryFilter)) return;
        if (!await EnsureCanDiscardChangesAsync()) return;
        SelectedCategoryFilter = node;
        PageIndex = 1;
        await LoadPageAsync();
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

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync(ProductMasterLineModel? line = null)
    {
        var target = line ?? SelectedMaster!;
        return _navigation.OpenDocumentAsync(
            ProductMasterDetailViewModel.RouteKey,
            $"product-master:{target.Uid:N}",
            new ProductMasterDocumentOpenRequest { ProductMasterId = target.Uid });
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

    /// <summary>验证后采集本批次同步授权，确认才提交；取消和失败均保留编辑。</summary>
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
            var request = Masters.CreateSaveRequest();
            if (!request.HasChanges) return;
            if (request.CreateItems.Count > 0 || request.UpdateItems.Count > 0)
            {
                var selection = await _syncDialog.ConfirmSaveAsync();
                if (!selection.Confirmed || _disposed) return;
                request.UpdateProductDescriptions = selection.UpdateProductDescriptions;
                request.UpdateProductDefaultSupplier = selection.UpdateProductDefaultSupplier;
            }
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

    private async Task LoadPageAsync() => await LoadPageCoreAsync();

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
            var request = new ProductMasterSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                CategoryIds = GetCategoryIds(SelectedCategoryFilter?.Source).ToList(),
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

    private void BuildCategoryFilters()
    {
        CategoryFilters = [new ProductMasterCategoryFilterNode("全部分类")];
        var all = CategoryFilters[0];
        CategoryFilters = [all, .. _cache.ProductCategoryTree.Select(node => new ProductMasterCategoryFilterNode(node.CategoryName, node))];
        SelectedCategoryFilter = all;
    }

    private static IEnumerable<Guid> GetCategoryIds(ProductCategoryCacheNode? node)
    {
        if (node is null) yield break;
        yield return node.Uid;
        foreach (var child in node.Children)
            foreach (var uid in GetCategoryIds(child)) yield return uid;
    }

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
    private bool CanDeleteSelected() => IsNotBusy && CanWrite && (Masters.CheckedCount > 0 || SelectedMaster is not null);
    private bool CanOpenDetail(ProductMasterLineModel? line = null)
    {
        var target = line ?? SelectedMaster;
        return IsNotBusy && target is not null && target.Uid != Guid.Empty;
    }

    /// <summary>取消查询并解除列表订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Masters.ListStateChanged -= OnListStateChanged;
        Neutrals.ListStateChanged -= OnNeutralListStateChanged;
        PropertyChanged -= OnManagerStateChanged;
        CommitPendingEdits = null;
        CancelNeutralQuery();
        var cts = Interlocked.Exchange(ref _queryCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
