using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

public sealed partial class CustomerProductManagerViewModel : ViewModelBaseEx
{
    private const int DefaultPageSize = 20;
    private readonly ICustomerProductApi _customerProductApi;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private long _loadVersion;
    private long _lookupVersion;
    private CustomerLineModel? _pendingCustomer;

    public CustomerProductManagerViewModel(
        ICustomerProductApi customerProductApi,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _customerProductApi = customerProductApi;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        Title = "客户产品";
        Products.PropertyChanged += OnListPropertyChanged;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddItemCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public CustomerProductListModel Products { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasChanges => Products.HasChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial CustomerLineModel? ActiveCustomer { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerProductLineModel? SelectedLine { get; set; }
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
    [ObservableProperty] public partial string StatusText { get; set; } = "请选择已保存客户后打开客户产品。";

    public event EventHandler<CustomerLineModel?>? SelectionRestoreRequested;

    partial void OnSelectedLineChanged(CustomerProductLineModel? value) => Products.SelectedItem = value;

    public void NotifyCustomerSelection(CustomerLineModel? customer)
    {
        _pendingCustomer = customer;
        if (IsOpen) _ = SwitchCustomerAsync(customer);
    }

    public async Task<bool> CloseForNavigationAsync()
    {
        if (!IsOpen) return true;
        if (!await ConfirmDiscardAsync()) return false;
        CloseCore();
        return true;
    }

    [RelayCommand]
    private async Task ToggleAsync(CustomerLineModel? customer)
    {
        if (IsOpen)
        {
            await CloseAsync();
            return;
        }

        var target = customer ?? _pendingCustomer;
        if (target is null || target.IsNew || target.Uid == Guid.Empty)
        {
            _messageService.ShowWarning("无法打开客户产品", "请先保存客户，再管理该客户的产品。将客户保存后即可打开抽屉。");
            return;
        }

        IsOpen = true;
        await LoadCustomerAsync(target);
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
        if (ActiveCustomer is null) return;
        SelectedLine = Products.AddNewItem(ActiveCustomer.Uid, ActiveCustomer.CustomerCode);
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
                "删除客户产品",
                $"确定删除 {lines.Length} 条客户产品吗？其中 {persistedCount} 条将在保存时删除。",
                "删除")) return;
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
                _messageService.ShowWarning("客户产品未保存", error);
                return;
            }

            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(
                () => _customerProductApi.BatchSaveAsync(request), "保存客户产品失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存客户产品失败", result.Message);
                return;
            }

            Products.ApplySaveResponse(result.Data);
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Products.Items.FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("客户产品已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
        }, "正在保存客户产品...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Products.RejectChanges();
        SelectedLine = Products.Items.FirstOrDefault();
        UpdateChangeState();
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
            Keyword = keyword.Trim(),
            ExcludeProductId = Guid.Empty,
            Limit = 20,
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
    private void CommitProduct(CustomerProductLineModel? line)
    {
        if (line is null || SelectedCandidate is null) return;
        var product = SelectedCandidate;
        line.SkuId = product.Uid;
        line.Sku = product.Sku ?? string.Empty;
        line.LinkSku = product.LinkSku ?? string.Empty;
        line.DescriptionEn = product.DescriptionEn ?? string.Empty;
        line.Description = FirstNonEmpty(product.DescriptionEn, product.DescriptionZh);
        line.FeaturesEn = product.FeaturesEn ?? string.Empty;
        line.CategoryName = product.CategoryName ?? string.Empty;
        line.MaterialType = product.MaterialType ?? string.Empty;
        line.MainImage = product.MainImage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line.CustomerPdDescription)) line.CustomerPdDescription = FirstNonEmpty(product.DescriptionEn, product.DescriptionZh);
        if (string.IsNullOrWhiteSpace(line.CustomerPdFeatures)) line.CustomerPdFeatures = product.FeaturesEn ?? string.Empty;
        if (!line.UsdPrice.HasValue) line.UsdPrice = product.USDPrice;
        if (!line.CnyPrice.HasValue) line.CnyPrice = product.CNYPrice;
        SelectedLine = line;
    }

    [RelayCommand]
    private void ClearProduct(CustomerProductLineModel? line)
    {
        if (line is null) return;
        line.SkuId = null;
        line.Sku = string.Empty;
        line.LinkSku = string.Empty;
        line.Description = string.Empty;
        line.DescriptionEn = string.Empty;
        line.FeaturesEn = string.Empty;
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

    private async Task SwitchCustomerAsync(CustomerLineModel? customer)
    {
        if (ReferenceEquals(customer, ActiveCustomer)) return;
        if (!await ConfirmDiscardAsync())
        {
            SelectionRestoreRequested?.Invoke(this, ActiveCustomer);
            return;
        }
        if (customer is null || customer.IsNew || customer.Uid == Guid.Empty)
        {
            CloseCore();
            return;
        }
        await LoadCustomerAsync(customer);
    }

    private async Task LoadCustomerAsync(CustomerLineModel customer)
    {
        Interlocked.Increment(ref _loadVersion);
        ClearLookupState();
        ActiveCustomer = customer;
        Keyword = string.Empty;
        PageIndex = 1;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        var customer = ActiveCustomer;
        if (!IsOpen || customer is null || customer.Uid == Guid.Empty) return;
        var version = Interlocked.Increment(ref _loadVersion);
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(() => _customerProductApi.GetPagedAsync(new CustomerProductSearchRequest
            {
                CustomerId = customer.Uid,
                Keyword = NullIfWhiteSpace(Keyword),
                PageIndex = PageIndex,
                PageSize = PageSize,
            }), "加载客户产品失败");
            if (version != Volatile.Read(ref _loadVersion)) return;
            if (!result.Success)
            {
                StatusText = result.Message;
                _messageService.ShowError("加载客户产品失败", result.Message);
                return;
            }

            Products.LoadFromDtos(result.Data?.Items ?? []);
            SelectedLine = Products.Items.FirstOrDefault();
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
            StatusText = $"客户 {customer.CustomerCode}，共 {TotalCount} 条客户产品。";
            UpdateChangeState();
        }, "正在加载客户产品...");
    }

    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        if (!Products.HasChanges) return true;
        var confirmed = await _dialogService.ConfirmAsync(
            "放弃客户产品修改", "当前客户产品有未保存修改，继续将放弃这些修改。", "继续");
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
        ActiveCustomer = null;
        _pendingCustomer = null;
        Keyword = string.Empty;
        TotalCount = 0;
        PageIndex = 1;
        IsOpen = false;
        StatusText = "客户产品抽屉已关闭。";
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
    }

    private bool CanRun() => IsNotBusy && IsOpen && ActiveCustomer is not null;
    private bool CanAdd() => IsNotBusy && CanWrite && IsOpen && ActiveCustomer is not null;
    private bool CanDelete() => IsNotBusy && CanWrite && (Products.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsNotBusy && CanWrite && Products.HasChanges;
    private bool CanReject() => IsNotBusy && Products.HasChanges;
    private static string FirstNonEmpty(string? first, string? second) => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
