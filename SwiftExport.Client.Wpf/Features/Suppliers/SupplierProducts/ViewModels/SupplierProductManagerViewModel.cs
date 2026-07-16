using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;

public sealed partial class SupplierProductManagerViewModel : ViewModelBaseEx
{
    private const int DefaultPageSize = 20;
    private readonly ISupplierProductApi _supplierProductApi;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private long _loadVersion;
    private long _lookupVersion;
    private SupplierLineModel? _pendingSupplier;

    public SupplierProductManagerViewModel(
        ISupplierProductApi supplierProductApi,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _supplierProductApi = supplierProductApi;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        Title = "供应商产品";
        Products.PropertyChanged += OnListPropertyChanged;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddItemCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(SetProductDefaultSupplierCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public SupplierProductListModel Products { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasChanges => Products.HasChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial SupplierLineModel? ActiveSupplier { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetProductDefaultSupplierCommand))]
    public partial SupplierProductLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial ProductLookupDto? SelectedCandidate { get; set; }
    [ObservableProperty] public partial bool IsLookupBusy { get; set; }
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
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
    [ObservableProperty] public partial string StatusText { get; set; } = "请选择已保存供应商后打开供应商产品。";

    public event EventHandler<SupplierLineModel?>? SelectionRestoreRequested;

    partial void OnSelectedLineChanged(SupplierProductLineModel? value) => Products.SelectedItem = value;

    public void NotifySupplierSelection(SupplierLineModel? supplier)
    {
        _pendingSupplier = supplier;
        if (IsOpen) _ = SwitchSupplierAsync(supplier);
    }

    public async Task<bool> CloseForNavigationAsync()
    {
        if (!IsOpen) return true;
        if (!await ConfirmDiscardAsync()) return false;
        CloseCore();
        return true;
    }

    [RelayCommand]
    private async Task ToggleAsync(SupplierLineModel? supplier)
    {
        if (IsOpen)
        {
            await CloseAsync();
            return;
        }

        var target = supplier ?? _pendingSupplier;
        if (target is null || target.IsNew || target.Uid == Guid.Empty)
        {
            _messageService.ShowWarning("无法打开供应商产品", "请先保存供应商，再管理该供应商的产品。");
            return;
        }

        IsOpen = true;
        await LoadSupplierAsync(target);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        CloseCore();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null) Keyword = query;
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddItem()
    {
        if (ActiveSupplier is null) return;
        SelectedLine = Products.AddNewItem(
            ActiveSupplier.Uid, ActiveSupplier.SupplierCode, ActiveSupplier.SupplierName,
            ActiveSupplier.Currency, ActiveSupplier.TaxRate);
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var lines = Products.CheckedCount > 0
            ? Products.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (lines.Length == 0) return;
        var persistedCount = lines.Count(line => !line.IsNew);
        if (persistedCount > 0 && !await _dialogService.ConfirmAsync(
                "删除供应商产品", $"确定删除 {lines.Length} 条供应商产品吗？其中 {persistedCount} 条将在保存时删除。", "删除")) return;
        foreach (var line in lines) Products.Remove(line);
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
                _messageService.ShowWarning("供应商产品未保存", error);
                return;
            }
            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(
                () => _supplierProductApi.BatchSaveAsync(request), "保存供应商产品失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存供应商产品失败", result.Message);
                return;
            }
            Products.ApplySaveResponse(result.Data);
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Products.Items.FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("供应商产品已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
        }, "正在保存供应商产品...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Products.RejectChanges();
        SelectedLine = Products.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSetProductDefaultSupplier))]
    private async Task SetProductDefaultSupplierAsync()
    {
        var selected = SelectedLine;
        if (selected is null
            || !await _dialogService.ConfirmAsync(
                "设置产品默认供应商",
                $"将 {selected.SupplierCode} 设置为产品 {selected.ProductSku} 的默认供应商。既有净需求计划不会自动改变，是否继续？",
                "设为默认"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var productResult = await _apiCallRunner.RunAsync(
                () => _productApi.GetByIdAsync(selected.SkuId),
                "读取产品并发版本失败");
            if (!productResult.Success || productResult.Data is null)
            {
                _messageService.ShowError("无法设置默认供应商", productResult.Message);
                return;
            }

            var result = await _apiCallRunner.RunAsync(
                () => _supplierProductApi.SetProductDefaultSupplierAsync(new SetProductDefaultSupplierCommand
                {
                    SupplierProductId = selected.Uid,
                    ProductId = selected.SkuId,
                    SupplierId = selected.SupplierId,
                    ExpectedProductVersion = productResult.Data.Version,
                }),
                "设置产品默认供应商失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("设置默认供应商失败", result.Message);
                return;
            }

            await LoadPageCoreAsync();
            StatusText = $"产品 {selected.ProductSku} 的默认供应商已设置为 {result.Data.SupplierCode}。";
            _messageService.ShowSuccess("默认供应商已更新", StatusText);
        }, "正在设置产品默认供应商...");
    }

    [RelayCommand]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        return ChangePageAsync(request.NewIndex, request.NewSize);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _lookupVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearLookupState(incrementVersion: false);
            return;
        }
        SelectedCandidate = null;
        IsLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(() => _productApi.SearchLookupAsync(new ProductLookupRequest
        {
            Keyword = keyword.Trim(), ExcludeProductId = Guid.Empty, Limit = 20,
        }), "搜索产品失败");
        if (version != Volatile.Read(ref _lookupVersion)) return;
        ProductCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var item in result.Data) ProductCandidates.Add(item);
        }
        IsLookupBusy = false;
    }

    [RelayCommand]
    private void CommitProduct(SupplierProductLineModel? line)
    {
        if (line is null || SelectedCandidate is null) return;
        var product = SelectedCandidate;
        line.SkuId = product.Uid;
        line.ProductSku = product.Sku ?? string.Empty;
        line.LinkSku = product.LinkSku ?? string.Empty;
        line.DescriptionEn = product.DescriptionEn ?? string.Empty;
        line.DescriptionZh = product.DescriptionZh ?? string.Empty;
        line.FeaturesZh = product.FeaturesZh ?? string.Empty;
        line.CategoryName = product.CategoryName ?? string.Empty;
        line.MaterialType = product.MaterialType ?? string.Empty;
        line.MainImage = product.MainImage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line.SupplierSkuDescriptionZh))
            line.SupplierSkuDescriptionZh = FirstNonEmpty(product.DescriptionZh, product.DescriptionEn);
        if (string.IsNullOrWhiteSpace(line.SupplierSkuFeaturesZh))
            line.SupplierSkuFeaturesZh = FirstNonEmpty(product.FeaturesZh, product.FeaturesEn);
        SelectedLine = line;
    }

    [RelayCommand]
    private void ClearProduct(SupplierProductLineModel? line)
    {
        if (line is null) return;
        line.SkuId = Guid.Empty;
        line.ProductSku = string.Empty;
        line.LinkSku = string.Empty;
        line.DescriptionEn = string.Empty;
        line.DescriptionZh = string.Empty;
        line.FeaturesZh = string.Empty;
        line.CategoryName = string.Empty;
        line.MaterialType = string.Empty;
        line.MainImage = string.Empty;
        SelectedLine = line;
    }

    private async Task ChangePageAsync(int index, int size)
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        PageIndex = Math.Max(1, index);
        PageSize = size <= 0 ? DefaultPageSize : size;
        await LoadPageAsync();
    }

    private async Task SwitchSupplierAsync(SupplierLineModel? supplier)
    {
        if (ReferenceEquals(supplier, ActiveSupplier)) return;
        if (!await ConfirmDiscardAsync())
        {
            SelectionRestoreRequested?.Invoke(this, ActiveSupplier);
            return;
        }
        if (supplier is null || supplier.IsNew || supplier.Uid == Guid.Empty)
        {
            CloseCore();
            return;
        }
        await LoadSupplierAsync(supplier);
    }

    private async Task LoadSupplierAsync(SupplierLineModel supplier)
    {
        Interlocked.Increment(ref _loadVersion);
        ClearLookupState();
        ActiveSupplier = supplier;
        Keyword = string.Empty;
        PageIndex = 1;
        await LoadPageAsync();
    }

    private Task LoadPageAsync() => RunBusyAsync(LoadPageCoreAsync, "正在加载供应商产品...");

    private async Task LoadPageCoreAsync()
    {
        var supplier = ActiveSupplier;
        if (!IsOpen || supplier is null || supplier.Uid == Guid.Empty) return;
        var version = Interlocked.Increment(ref _loadVersion);
        var result = await _apiCallRunner.RunAsync(() => _supplierProductApi.GetPagedAsync(new SupplierProductSearchRequest
        {
            SupplierId = supplier.Uid,
            Keyword = NullIfWhiteSpace(Keyword),
            PageIndex = PageIndex,
            PageSize = PageSize,
        }), "加载供应商产品失败");
        if (version != Volatile.Read(ref _loadVersion)) return;
        if (!result.Success)
        {
            StatusText = result.Message;
            _messageService.ShowError("加载供应商产品失败", result.Message);
            return;
        }
        Products.LoadFromDtos(result.Data?.Items ?? []);
        SelectedLine = Products.Items.FirstOrDefault();
        TotalCount = result.Data?.TotalCount ?? 0;
        PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
        PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
        StatusText = $"供应商 {supplier.SupplierCode}，共 {TotalCount} 条供应商产品。";
        UpdateChangeState();
    }

    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        if (!Products.HasChanges) return true;
        var confirmed = await _dialogService.ConfirmAsync(
            "放弃供应商产品修改", "当前供应商产品有未保存修改，继续将放弃这些修改。", "继续");
        if (confirmed) Products.RejectChanges();
        return confirmed;
    }

    private Task<bool> ConfirmDiscardAsync() => EnsureCanDiscardChangesAsync();

    private void CloseCore()
    {
        Interlocked.Increment(ref _loadVersion);
        ClearLookupState();
        Products.Clear();
        SelectedLine = null;
        ActiveSupplier = null;
        _pendingSupplier = null;
        Keyword = string.Empty;
        TotalCount = 0;
        PageIndex = 1;
        IsOpen = false;
        StatusText = "供应商产品抽屉已关闭。";
        UpdateChangeState();
    }

    private void ClearLookupState(bool incrementVersion = true)
    {
        if (incrementVersion) Interlocked.Increment(ref _lookupVersion);
        ProductCandidates.Clear();
        SelectedCandidate = null;
        IsLookupBusy = false;
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    private void UpdateChangeState()
    {
        IsDirty = Products.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        SetProductDefaultSupplierCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy && IsOpen && ActiveSupplier is not null;
    private bool CanAdd() => IsNotBusy && CanWrite && IsOpen && ActiveSupplier is not null;
    private bool CanDelete() => IsNotBusy && CanWrite && (Products.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsNotBusy && CanWrite && Products.HasChanges;
    private bool CanReject() => IsNotBusy && Products.HasChanges;
    private bool CanSetProductDefaultSupplier() => CanWrite
        && IsNotBusy
        && IsOpen
        && !Products.HasChanges
        && ActiveSupplier is not null
        && SelectedLine is { IsNew: false, IsDeleted: false, IsProductDefaultSupplier: false }
        && SelectedLine.Uid != Guid.Empty
        && SelectedLine.SkuId != Guid.Empty
        && SelectedLine.SupplierId == ActiveSupplier.Uid;
    private static string FirstNonEmpty(string? first, string? second) => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
