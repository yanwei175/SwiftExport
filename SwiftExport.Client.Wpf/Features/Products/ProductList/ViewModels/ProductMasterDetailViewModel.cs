using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>主型号详情文档模型，负责头部与中性产品的原子保存。</summary>
public sealed partial class ProductMasterDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>获取主型号详情文档路由键。</summary>
    public const string RouteKey = "product-master-detail";
    private readonly IProductMasterApi _api;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IProductMasterSyncDialogService _syncDialog;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _loadCts;
    private long _loadSequence;
    private long _supplierSequence;
    private bool _disposed;

    /// <summary>初始化详情页依赖和状态订阅。</summary>
    public ProductMasterDetailViewModel(
        IProductMasterApi api,
        ISupplierApi supplierApi,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        IProductMasterSyncDialogService syncDialog)
    {
        _api = api;
        _supplierApi = supplierApi;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _syncDialog = syncDialog;
        _navigation = navigation;
        Title = "父级详情";
        Header.PropertyChanged += OnHeaderPropertyChanged;
        Neutrals.ListStateChanged += OnNeutralListStateChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(AddNeutralCommand);
        RegisterBusyAwareCommand(DeleteNeutralCommand);
    }

    /// <summary>获取主型号头部编辑模型。</summary>
    public ProductMasterEditModel Header { get; } = new();
    /// <summary>获取中性产品明细集合。</summary>
    public ProductNeutralListModel Neutrals { get; } = new();
    /// <summary>获取基础资料缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache => _cache;
    /// <summary>获取供应商搜索候选。</summary>
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    /// <summary>获取文档标题。</summary>
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.MasterSku) ? "新建主型号" : $"主型号 · {Header.MasterSku}";
    /// <summary>获取头部或明细是否存在未保存变更。</summary>
    public bool HasUnsavedChanges => Header.IsDirty || Neutrals.HasChanges;
    /// <summary>获取当前是否为新建文档。</summary>
    public bool IsCreateMode => Header.Uid == Guid.Empty;

    /// <summary>预览当前未删除中性产品中的默认主图，随编辑实时变化，不从服务端重新查询。</summary>
    public string? MainImagePath => Neutrals.Items.FirstOrDefault(line => line.IsMainImage)?.ImagePath;

    /// <summary>获取或设置当前中性产品行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNeutralCommand))]
    public partial ProductNeutralLineModel? SelectedNeutral { get; set; }
    /// <summary>获取或设置头部分类选择。</summary>
    [ObservableProperty] public partial ProductCategoryCacheNode? SelectedCategory { get; set; }
    /// <summary>获取或设置头部默认供应商选择。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    /// <summary>获取或设置中性产品颜色编辑器选择。</summary>
    [ObservableProperty] public partial ProductColorDto? EditColor { get; set; }
    /// <summary>获取供应商 Lookup 是否正在查询。</summary>
    [ObservableProperty] public partial bool IsSupplierLookupBusy { get; set; }
    /// <summary>获取详情加载与保存状态文本。</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = "正在准备主型号详情。";
    /// <summary>获取或设置中性产品 DataGrid 是否使用格子选择模式。</summary>
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; } = false;

    /// <summary>根据文档打开参数初始化新建状态或加载现有聚合。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var cacheResult = await _cache.EnsureLoadedAsync();
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        if (parameter is not ProductMasterDocumentOpenRequest request || request.ProductMasterId == Guid.Empty)
        {
            InitializeCreate();
            return;
        }
        await LoadAsync(request.ProductMasterId, cancellationToken);
    }

    /// <summary>关闭详情前检查真实变更集，写入进行中时拒绝关闭。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("放弃未保存变更", "主型号详情有未保存修改，确定关闭吗？", "关闭");
    }

    partial void OnSelectedNeutralChanged(ProductNeutralLineModel? value) => Neutrals.SelectedItem = value;

    [RelayCommand]
    private void AddNeutral()
    {
        SelectedNeutral = Neutrals.AddNewNeutral(Header.Uid);
        NotifyDocumentState();
    }

    /// <summary>新增行直接移除；已保存行确认后软删除，完成后选择剩余最后一行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteNeutral))]
    private async Task DeleteNeutralAsync()
    {
        var lines = Neutrals.CheckedItems.Count > 0
            ? Neutrals.CheckedItems.ToArray()
            : SelectedNeutral is null ? [] : [SelectedNeutral];
        if (lines.Length == 0) return;
        if (lines.Any(line => !line.IsNew)
            && !await _dialogs.ConfirmAsync("删除中性产品", $"确定删除选中的 {lines.Length} 条中性产品吗？保存后生效。", "删除")) return;
        foreach (var line in lines) Neutrals.Remove(line);
        SelectedNeutral = Neutrals.Items.LastOrDefault();
        NotifyDocumentState();
    }

    /// <summary>验证聚合并采集同步授权，确认后原子保存，取消及失败不接纳基线。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var headerError = Header.ValidateRow();
            var lineError = await Neutrals.ValidateAllAsync();
            var error = headerError ?? lineError;
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("主型号未保存", error);
                return;
            }
            var request = new SaveProductMasterDetailRequest
            {
                ProductMasterId = Header.Uid,
                ExpectedVersion = Header.Uid == Guid.Empty ? 0 : Header.Version,
                Master = Header.ToWriteDto()
            };
            Neutrals.ApplyChanges(request);
            var selection = await _syncDialog.ConfirmSaveAsync();
            if (!selection.Confirmed || _disposed) return;
            request.UpdateProductDescriptions = selection.UpdateProductDescriptions;
            request.UpdateProductDefaultSupplier = selection.UpdateProductDefaultSupplier;
            var result = await _runner.RunAsync(() => _api.SaveDetailAsync(request), "保存主型号详情失败");
            if (!result.Success || result.Data?.Detail is null)
            {
                ValidationSummary = result.Message;
                if (result.Error?.ErrorCodeIs(ServiceErrorCodes.VersionConflict) == true)
                    _messages.ShowWarning("版本冲突", "数据已被其他用户修改，本地编辑已保留。请决定是否刷新。");
                else
                    _messages.ShowError("保存主型号详情失败", result.Message);
                return;
            }
            var wasCreate = Header.Uid == Guid.Empty;
            AcceptDetail(result.Data.Detail);
            if (wasCreate) _navigation.UpdateDocumentKey(this, $"product-master:{Header.Uid:N}");
            _messages.ShowSuccess("主型号已保存", Header.MasterSku);
        }, "正在保存主型号详情...");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Header.Uid == Guid.Empty) return;
        if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("放弃未保存变更", "刷新将放弃当前详情修改，是否继续？", "刷新")) return;
        await LoadAsync(Header.Uid, CancellationToken.None);
    }

    /// <summary>撤销头部及明细，并同步 Lookup 选择，避免控件失焦后恢复已撤销的供应商。</summary>
    [RelayCommand]
    private void RejectDetailChanges()
    {
        Header.RejectChanges();
        SyncSelectedSupplier();
        SelectedCategory = _cache.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
        Neutrals.RejectChanges();
        SelectedNeutral = Neutrals.Items.FirstOrDefault();
        NotifyDocumentState();
    }

    [RelayCommand]
    private Task CloseDocumentAsync() => _navigation.CloseAsync(
        _navigation.OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.ContentViewModel, this)));

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
        if (sequence != Volatile.Read(ref _supplierSequence)) return;
        SupplierCandidates.Clear();
        foreach (var item in result.Success ? result.Data?.Items ?? [] : []) SupplierCandidates.Add(item);
        IsSupplierLookupBusy = false;
    }

    /// <summary>只刷新共享分类缓存，不重新加载详情或接纳未保存编辑；关闭后不再反馈结果。</summary>
    [RelayCommand]
    private async Task RefreshProductCategoriesAsync()
    {
        if (_disposed || RefreshProductCategoriesCommand.IsRunning) return;
        // 缓存刷新是会话级共享操作，当前接口不接受取消；页面只消费存活期间的结果。
        var result = await _cache.RefreshProductCategoriesAsync();
        if (_disposed) return;
        if (!result.Success)
        {
            _messages.ShowError("刷新产品分类失败", result.Message);
            return;
        }
        SelectedCategory = _cache.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
    }

    /// <summary>用户确认树形候选后才将分类标识与名称写入头部。</summary>
    [RelayCommand]
    private void CommitCategory()
    {
        if (SelectedCategory is null) return;
        Header.CategoryId = SelectedCategory.Uid;
        Header.CategoryName = SelectedCategory.CategoryName;
    }

    /// <summary>清空头部分类及控件选择，使用空 Guid 表示未设置分类。</summary>
    [RelayCommand]
    private void ClearCategory()
    {
        Header.CategoryId = Guid.Empty;
        Header.CategoryName = string.Empty;
        SelectedCategory = null;
    }

    /// <summary>仅在确认候选时同步供应商标识和名称，不将搜索文本写入头部。</summary>
    [RelayCommand]
    private void CommitSupplier()
    {
        if (SelectedSupplier is null) return;
        Header.DefaultSupplierId = SelectedSupplier.Uid;
        Header.DefaultSupplierName = SelectedSupplier.SupplierName ?? string.Empty;
    }

    /// <summary>显式清空可选供应商，同时清除头部字段和控件选择。</summary>
    [RelayCommand]
    private void ClearSupplier()
    {
        Header.DefaultSupplierId = Guid.Empty;
        Header.DefaultSupplierName = string.Empty;
        SelectedSupplier = null;
    }

    /// <summary>根据已加载或撤销后的头部重建显示选择，不触发提交或重新接纳编辑基线。</summary>
    private void SyncSelectedSupplier()
    {
        SelectedSupplier = Header.DefaultSupplierId == Guid.Empty
            ? null
            : new SupplierDto { Uid = Header.DefaultSupplierId, SupplierName = Header.DefaultSupplierName };
    }

    [RelayCommand]
    private void CommitColor(ProductNeutralLineModel? line)
    {
        if (line is null || EditColor is null) return;
        line.ColorId = EditColor.Uid;
        line.ColorName = EditColor.ColorNameZh ?? EditColor.ColorNameEn ?? string.Empty;
        EditColor = null;
    }

    [RelayCommand]
    private void SetMainImage(ProductNeutralLineModel? line)
    {
        if (line is null) return;
        Neutrals.SetMainImage(line);
    }

    private void InitializeCreate()
    {
        Header.Load(new ProductMasterDetailDto { Master = new ProductMasterWriteDto { DefaultPcsPerPackage = 1 } });
        Header.MarkAsAdded();
        Neutrals.Clear();
        AddNeutral();
        StatusText = "请维护主型号头部和中性产品。";
        NotifyDocumentState();
    }

    private async Task LoadAsync(Guid productMasterId, CancellationToken externalToken)
    {
        var sequence = Interlocked.Increment(ref _loadSequence);
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var previousCts = Interlocked.Exchange(ref _loadCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(productMasterId, currentCts.Token), currentCts.Token, "加载主型号详情失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _loadSequence)) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载主型号详情失败", result.Message);
                return;
            }
            AcceptDetail(result.Data);
        }, "正在加载主型号详情...");
    }

    private void AcceptDetail(ProductMasterDetailDto detail)
    {
        Header.Load(detail);
        Neutrals.LoadFromDtos(detail.Neutrals ?? []);
        SelectedNeutral = Neutrals.Items.FirstOrDefault();
        SelectedCategory = _cache.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
        SyncSelectedSupplier();
        StatusText = $"已加载 {Neutrals.Items.Count} 条中性产品。";
        AcceptChanges();
        NotifyDocumentState();
    }

    private void OnHeaderPropertyChanged(object? sender, PropertyChangedEventArgs e) => NotifyDocumentState();
    /// <summary>明细属性、增删或回滚变化时同步主图预览及文档状态。</summary>
    private void OnNeutralListStateChanged(object? sender, ListStateChangedEventArgs<ProductNeutralLineModel> e)
    {
        OnPropertyChanged(nameof(MainImagePath));
        NotifyDocumentState();
    }

    private void NotifyDocumentState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsCreateMode));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteNeutralCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => IsNotBusy && CanWrite && HasUnsavedChanges;
    private bool CanDeleteNeutral() => IsNotBusy && CanWrite && (Neutrals.CheckedCount > 0 || SelectedNeutral is not null);

    /// <summary>取消详情加载并解除模型事件订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Header.PropertyChanged -= OnHeaderPropertyChanged;
        Neutrals.ListStateChanged -= OnNeutralListStateChanged;
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}

internal static class ProductMasterServiceResultExtensions
{
    public static bool ErrorCodeIs(this ServiceErrorDto error, string errorCode) =>
        string.Equals(error.Code, errorCode, StringComparison.Ordinal);
}
