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
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

/// <summary>统一持有客户选择、客户产品草稿、分页及页签生命周期，业务关系和保存保护不跨 VM 转发。</summary>
public sealed partial class CustomerProductManagerViewModel : ViewModelBaseEx, IDocumentTabViewModel, IAsyncInitializable, IDisposable
{
    private const int DefaultPageSize = 20;
    private readonly ICustomerProductApi _customerProductApi;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private long _loadVersion;
    private long _lookupVersion;

    /// <summary>注入客户产品 API、候选缓存与页面反馈，由同一实例拥有全部订阅和读取生命周期。</summary>
    public CustomerProductManagerViewModel(
        ICustomerProductApi customerProductApi,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache)
    {
        _customerProductApi = customerProductApi;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        Title = "客户产品管理";
        Products.PropertyChanged += OnListPropertyChanged;
        PropertyChanged += OnPagePropertyChanged;
        InitializeCheckSelection();
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddItemCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(ClearCustomerCommand);
    }

    public CustomerProductListModel Products { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasChanges => Products.HasChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial CustomerLineModel? ActiveCustomer { get; set; }
    /// <summary>查询单独占有加载状态，客户可替换在途查询，写入 Busy 仍阻止上下文切换。</summary>
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerProductLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial ProductLookupDto? SelectedCandidate { get; set; }
    [ObservableProperty] public partial bool IsLookupBusy { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UncheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    public partial bool IsCellSelectionMode { get; set; }
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

    partial void OnSelectedLineChanged(CustomerProductLineModel? value) => Products.SelectedItem = value;

    /// <summary>显式选择已保存客户，切换前确认现有产品草稿。</summary>
    public async Task<bool> SelectCustomerAsync(CustomerLineModel? customer)
    {
        if (!CanChangeCustomer || !(CommitPendingEdits?.Invoke() ?? true)) return false;
        var target = customer;
        if (target is null || target.IsNew || target.Uid == Guid.Empty)
        {
            _messageService.ShowWarning("无法管理客户产品", "请先选择一个已保存客户。");
            return false;
        }

        if (IsOpen && ActiveCustomer?.Uid == target.Uid)
        {
            return true;
        }

        if (IsOpen && !await ConfirmDiscardAsync())
        {
            return false;
        }

        if (_disposed || IsBusy) return false;
        IsOpen = true;
        await LoadCustomerAsync(target);
        return true;
    }

    /// <summary>导航关闭前确认产品草稿，拒绝时保留当前编辑器。</summary>
    public Task<bool> CloseForNavigationAsync() => CanCloseAsync(CancellationToken.None);

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
        SelectedLine = Products.AddNewItem(
            ActiveCustomer.Uid,
            ActiveCustomer.CustomerCode);
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

    /// <summary>提交原生输入后冻结请求；写入期间保持唯一客户上下文，释放后不接纳回执。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !(CommitPendingEdits?.Invoke() ?? true)) return;
        await RunWriteAsync(async () =>
        {
            var error = await Products.ValidateAllAsync();
            if (_disposed) return;
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("客户产品未保存", error);
                return;
            }

            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(
                () => _customerProductApi.BatchSaveAsync(request), "保存客户产品失败");
            if (_disposed) return;
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

    /// <summary>候选读取独立取消，切客户或释放使旧结果失效。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        if (_disposed || IsBusy || IsLoading) return;
        CancelRead(ref _lookupCts);
        var version = Interlocked.Increment(ref _lookupVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearLookupState(incrementVersion: false);
            return;
        }

        SelectedCandidate = null;
        IsLookupBusy = true;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cts.Token; _lookupCts = cts;
        try
        {
            var result = await _apiCallRunner.RunAsync(() => _productApi.SearchLookupAsync(new ProductLookupRequest
            { Keyword = keyword.Trim(), ExcludeProductId = Guid.Empty, Limit = 20 }, token), token, "搜索产品失败");
            if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _lookupVersion)) return;
            ProductCandidates.Clear();
            if (result.Success && result.Data is not null)
                foreach (var item in result.Data) ProductCandidates.Add(item);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!_disposed && ReferenceEquals(_lookupCts, cts)) IsLookupBusy = false;
        }
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

    /// <summary>在新客户查询前清除旧客户数据显示，再由最新请求接纳产品。</summary>
    private async Task LoadCustomerAsync(CustomerLineModel customer)
    {
        Interlocked.Increment(ref _loadVersion);
        ClearLookupState();
        Products.Clear(); SelectedLine = null; TotalCount = 0;
        ActiveCustomer = customer;
        Keyword = string.Empty;
        PageIndex = 1;
        await LoadPageAsync();
    }

    /// <summary>查询允许客户替换在途等待，身份、序号和存活状态共同守护回写。</summary>
    private async Task LoadPageAsync()
    {
        var customer = ActiveCustomer;
        if (_disposed || IsBusy || !IsOpen || customer is null || customer.Uid == Guid.Empty) return;
        var version = Interlocked.Increment(ref _loadVersion);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cts.Token; var old = Interlocked.Exchange(ref _pageCts, cts); old?.Cancel(); old?.Dispose();
        IsLoading = true;
        StatusText = $"正在加载客户 {customer.CustomerCode} 的产品...";
        try
        {
            // 旧客户产品接口不支持 HTTP Token；取消等待，同时用请求身份阻止旧响应回写。
            var result = await _apiCallRunner.RunAsync(() => _customerProductApi.GetPagedAsync(new CustomerProductSearchRequest
            {
                CustomerId = customer.Uid,
                Keyword = NullIfWhiteSpace(Keyword),
                PageIndex = PageIndex,
                PageSize = PageSize,
            }).WaitAsync(token), token, "加载客户产品失败");
            if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || ActiveCustomer?.Uid != customer.Uid) return;
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
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!_disposed && ReferenceEquals(_pageCts, cts)) IsLoading = false;
        }
    }

    /// <summary>同一确认拥有草稿放弃权，输入错误或写入 Busy 都不会替换当前编辑上下文。</summary>
    private async Task<bool> EnsureCanDiscardChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanChangeCustomer || !(CommitPendingEdits?.Invoke() ?? true)) return false;
        if (!Products.HasChanges) return true;
        IsPrompting = true;
        try
        {
            var confirmed = await _dialogService.ConfirmAsync("放弃客户产品修改", "当前客户产品有未保存修改，继续将放弃这些修改。", "继续", cancellationToken: cancellationToken);
            if (_disposed || IsBusy || !confirmed) return false;
            Products.RejectChanges(); return true;
        }
        finally { if (!_disposed) IsPrompting = false; }
    }

    private Task<bool> ConfirmDiscardAsync() => EnsureCanDiscardChangesAsync();

    /// <summary>只清空本页客户与私有产品状态，并失效在途读取，不修改共享候选缓存。</summary>
    private void CloseCore()
    {
        Interlocked.Increment(ref _loadVersion);
        CancelRead(ref _pageCts); IsLoading = false;
        ClearLookupState();
        Products.Clear();
        SelectedLine = null;
        ActiveCustomer = null;
        Keyword = string.Empty;
        TotalCount = 0;
        PageIndex = 1;
        IsOpen = false;
        StatusText = "客户已清空，请选择已保存客户。";
        UpdateChangeState();
    }

    private void ClearLookupState(bool incrementVersion = true)
    {
        CancelRead(ref _lookupCts);
        if (incrementVersion) Interlocked.Increment(ref _lookupVersion);
        ProductCandidates.Clear();
        SelectedCandidate = null;
        IsLookupBusy = false;
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    /// <summary>直接发布集合的草稿和页签状态，不通过嵌套 VM 镜像。</summary>
    private void UpdateChangeState()
    {
        IsDirty = Products.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        NotifyCapabilities();
    }

    /// <summary>统一刷新同一页面命令和可编辑状态，不镜像第二个 VM。</summary>
    private void NotifyCapabilities()
    {
        RefreshCheckState();
        OnPropertyChanged(nameof(CanChangeCustomer)); OnPropertyChanged(nameof(CanEditProducts));
        SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); AddItemCommand.NotifyCanExecuteChanged();
        PageChangeCommand.NotifyCanExecuteChanged(); ClearCustomerCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => CanEditProducts && ActiveCustomer is not null;
    private bool CanAdd() => CanRun() && CanWrite;
    private bool CanDelete() => CanAdd() && (Products.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => CanAdd() && Products.HasChanges;
    private bool CanReject() => CanRun() && Products.HasChanges;
    private static string FirstNonEmpty(string? first, string? second) => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => CanAdd()
        && request is not null && request.Fields.All(field => field is nameof(CustomerProductLineModel.CustomerSku)
            or nameof(CustomerProductLineModel.CustomerPdDescription)
            or nameof(CustomerProductLineModel.CustomerPdSeries)
            or nameof(CustomerProductLineModel.CustomerColorName)
            or nameof(CustomerProductLineModel.CustomerPdFeatures)
            or nameof(CustomerProductLineModel.CustomerBarcode1)
            or nameof(CustomerProductLineModel.CustomerBarcode2)
            or nameof(CustomerProductLineModel.Note)
            or nameof(CustomerProductLineModel.Moq)
            or nameof(CustomerProductLineModel.LeadTimeDays)
            or nameof(CustomerProductLineModel.UsdPrice)
            or nameof(CustomerProductLineModel.CnyPrice))
        && request.TryGetRows<CustomerProductLineModel>(Products.Items, out _, out var targets)
        && targets.Any(row => !row.IsDeleted && !row.IsReadOnly);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<CustomerProductLineModel>(Products.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(row => !row.IsDeleted && !row.IsReadOnly))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(CustomerProductLineModel.CustomerSku): target.CustomerSku = source.CustomerSku; break;
                    case nameof(CustomerProductLineModel.CustomerPdDescription): target.CustomerPdDescription = source.CustomerPdDescription; break;
                    case nameof(CustomerProductLineModel.CustomerPdSeries): target.CustomerPdSeries = source.CustomerPdSeries; break;
                    case nameof(CustomerProductLineModel.CustomerColorName): target.CustomerColorName = source.CustomerColorName; break;
                    case nameof(CustomerProductLineModel.CustomerPdFeatures): target.CustomerPdFeatures = source.CustomerPdFeatures; break;
                    case nameof(CustomerProductLineModel.CustomerBarcode1): target.CustomerBarcode1 = source.CustomerBarcode1; break;
                    case nameof(CustomerProductLineModel.CustomerBarcode2): target.CustomerBarcode2 = source.CustomerBarcode2; break;
                    case nameof(CustomerProductLineModel.Note): target.Note = source.Note; break;
                    case nameof(CustomerProductLineModel.Moq): target.Moq = source.Moq; break;
                    case nameof(CustomerProductLineModel.LeadTimeDays): target.LeadTimeDays = source.LeadTimeDays; break;
                    case nameof(CustomerProductLineModel.UsdPrice): target.UsdPrice = source.UsdPrice; break;
                    case nameof(CustomerProductLineModel.CnyPrice): target.CnyPrice = source.CnyPrice; break;
                }
            }
        }
    }
}
