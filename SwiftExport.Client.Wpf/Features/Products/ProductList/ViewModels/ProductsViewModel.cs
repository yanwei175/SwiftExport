using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Resources.Controls;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

public sealed partial class ProductsViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private readonly IProductApi _productApi;
    private readonly ICustomerApi _customerApi;
    private readonly IProductColorApi _colorApi;
    private readonly IProductCategoryApi _categoryApi;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private bool _isInitialized;
    private bool _restoringSelection;
    private long _customerSearchVersion;
    private long _colorSearchVersion;
    private long _supplierSearchVersion;

    public ProductsViewModel(
        IProductApi productApi,
        ICustomerApi customerApi,
        IProductColorApi colorApi,
        IProductCategoryApi categoryApi,
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        BomDrawerViewModel bomDrawer)
    {
        _productApi = productApi;
        _customerApi = customerApi;
        _colorApi = colorApi;
        _categoryApi = categoryApi;
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        BomDrawer = bomDrawer;
        Title = "产品资料";
        Products.PropertyChanged += OnProductsPropertyChanged;
        BomDrawer.SelectionRestoreRequested += (_, product) => RestoreSelection(product);

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectProductChangesCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ProductListModel Products { get; } = new();
    public BomDrawerViewModel BomDrawer { get; }
    public ObservableCollection<CustomerDto> CustomerCandidates { get; } = [];
    public ObservableCollection<ProductColorDto> ColorCandidates { get; } = [];
    public ObservableCollection<ProductCategoryDto> CategoryCandidates { get; } = [];
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    public IReadOnlyList<string> MaterialTypes { get; } = MaterialTypesEnum.MaterialTypes;
    [ObservableProperty]
    public partial ObservableCollection<string> FilterMaterialTypes { get; set; } = [];
    public IReadOnlyList<string> SupplyTypes { get; } = SupplyTypeEnums.SupplyTypes;

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty]
    public partial CustomerDto? FilterCustomer { get; set; }
    [ObservableProperty]
    public partial ProductColorDto? FilterColor { get; set; }
    [ObservableProperty]
    public partial ProductCategoryDto? FilterCategory { get; set; }
    [ObservableProperty]
    public partial ProductCategoryDto? EditCategory { get; set; }
    [ObservableProperty]
    public partial ProductColorDto? EditColor { get; set; }
    [ObservableProperty]
    public partial CustomerDto? EditCustomer { get; set; }
    [ObservableProperty]
    public partial SupplierDto? EditSupplier { get; set; }
    [ObservableProperty]
    public partial bool IsCustomerLookupBusy { get; set; }
    [ObservableProperty]
    public partial bool IsColorLookupBusy { get; set; }
    [ObservableProperty]
    public partial bool IsSupplierLookupBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductLineModel? SelectedLine { get; set; }

    [ObservableProperty]
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
        await LoadCategoriesAsync();
        await LoadPageAsync(false);
    }

    partial void OnSelectedLineChanged(ProductLineModel? value)
    {
        Products.SelectedItem = value;
        if (!_restoringSelection) BomDrawer.NotifyProductSelection(value);
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
    private Task RefreshAsync() => LoadPageAsync(true);

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        SelectedLine = Products.AddNewProduct();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
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

            Products.AcceptChanges();
            UpdateChangeState();
            _messageService.ShowSuccess("产品资料已保存", $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
            await LoadPageCoreAsync();
        }, "正在保存产品资料...");
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) return;
        Keyword = string.Empty;
        FilterCustomer = null;
        FilterColor = null;
        FilterCategory = null;
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

    [RelayCommand] private Task SearchCustomersAsync(string? keyword) => LoadCustomersAsync(keyword);
    [RelayCommand] private Task SearchColorsAsync(string? keyword) => LoadColorsAsync(keyword);
    [RelayCommand] private Task SearchSuppliersAsync(string? keyword) => LoadSuppliersAsync(keyword);

    [RelayCommand]
    private void CommitCategory(ProductLineModel? line)
    {
        if (line is null || EditCategory is null) return;
        line.CategoryId = EditCategory.Uid; line.CategoryName = EditCategory.CategoryName ?? string.Empty;
    }

    [RelayCommand]
    private void CommitColor(ProductLineModel? line)
    {
        if (line is null || EditColor is null) return;
        line.ColorId = EditColor.Uid; line.Finish = EditColor.ColorNameZh ?? EditColor.ColorNameEn ?? string.Empty;
    }

    [RelayCommand]
    private void CommitCustomer(ProductLineModel? line)
    {
        if (line is null || EditCustomer is null) return;
        line.DefaultCustomerCode = EditCustomer.CustomerCode ?? string.Empty;
    }

    [RelayCommand]
    private void CommitSupplier(ProductLineModel? line)
    {
        if (line is null || EditSupplier is null) return;
        line.DefaultSupplierId = EditSupplier.Uid;
        line.DefaultSupplierCode = EditSupplier.SupplierCode ?? string.Empty;
        line.SupplierName = EditSupplier.SupplierName ?? string.Empty;
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
            Keyword = NullIfWhiteSpace(Keyword), CustomerId = FilterCustomer?.Uid ?? Guid.Empty,
            ColorCodeUnique = FilterColor?.ColorCodeUnique, CategoryId = FilterCategory?.Uid ?? Guid.Empty,
            MaterialTypeList = FilterMaterialTypes.ToList(), PageIndex = PageIndex, PageSize = PageSize,
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

    private async Task LoadCategoriesAsync()
    {
        var result = await _apiCallRunner.RunAsync(() => _categoryApi.GetAllAsync(), "加载产品分类失败");
        CategoryCandidates.ReplaceWith(result.Success ? result.Data ?? [] : []);
    }

    private async Task LoadCustomersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _customerSearchVersion); IsCustomerLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(() => _customerApi.GetPagedAsync(new CustomerSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 30 }), "搜索客户失败");
        if (version == Volatile.Read(ref _customerSearchVersion)) CustomerCandidates.ReplaceWith(result.Success ? result.Data?.Items ?? [] : []);
        IsCustomerLookupBusy = false;
    }

    private async Task LoadColorsAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _colorSearchVersion); IsColorLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(() => _colorApi.GetPagedAsync(new ProductColorSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 30 }), "搜索颜色失败");
        if (version == Volatile.Read(ref _colorSearchVersion)) ColorCandidates.ReplaceWith(result.Success ? result.Data?.Items ?? [] : []);
        IsColorLookupBusy = false;
    }

    private async Task LoadSuppliersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _supplierSearchVersion); IsSupplierLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(() => _supplierApi.GetPagedAsync(new SupplierSearchRequest { Keyword = keyword, PageIndex = 1, PageSize = 30 }), "搜索供应商失败");
        if (version == Volatile.Read(ref _supplierSearchVersion)) SupplierCandidates.ReplaceWith(result.Success ? result.Data?.Items ?? [] : []);
        IsSupplierLookupBusy = false;
    }

    private async Task<bool> EnsureCanDiscardChangesAsync() =>
        !Products.HasChanges || await _dialogService.ConfirmAsync("放弃未保存变更", "当前产品列表有未保存修改，继续将放弃这些修改。", "继续");

    private void RestoreSelection(ProductLineModel? product)
    {
        _restoringSelection = true;
        try { SelectedLine = product; }
        finally { _restoringSelection = false; }
    }

    private void OnProductsPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();
    private void UpdateChangeState()
    {
        IsDirty = Products.HasChanges; OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged(); RejectProductChangesCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanWriteCommand() => IsNotBusy && CanWrite;
    private bool CanDeleteSelected() =>
        IsNotBusy && CanWrite && (Products.CheckedCount > 0 || SelectedLine is not null);
    private bool CanRejectChanges() => IsNotBusy && Products.HasChanges;
    private bool CanSave() => IsNotBusy && CanWrite && Products.HasChanges;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }
}
