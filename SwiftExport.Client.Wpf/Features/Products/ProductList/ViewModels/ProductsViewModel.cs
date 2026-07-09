using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Controls;
using SwiftExportClient.Features.Products.Categories.Services;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

public sealed class ProductsViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IProductCategoryManagerDialogService _categoryManagerDialogService;
    private bool _isInitialized;
    private string _keyword = string.Empty;
    private string _categoryName = string.Empty;
    private string _materialType = string.Empty;
    private int _pageIndex = 1;
    private int _pageSize = DefaultPageSize;
    private int _totalCount;
    private ProductLineModel? _selectedLine;
    private string _resultSummary = "尚未加载产品资料。";

    public ProductsViewModel(
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IProductCategoryManagerDialogService categoryManagerDialogService)
    {
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _categoryManagerDialogService = categoryManagerDialogService;

        Title = "产品资料";
        Lines = new BaseListModel<ProductLineModel, ProductDto>(_ => new ProductLineModel());
        Lines.PropertyChanged += OnLinesPropertyChanged;

        SearchCommand = new AsyncRelayCommand(SearchAsync, CanRunCommand);
        ResetCommand = new AsyncRelayCommand(ResetAsync, CanRunCommand);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunCommand);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, CanGoPreviousPage);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync, CanGoNextPage);
        PageChangeCommand = new AsyncRelayCommand<PageChangeRequest>(PageChangeAsync, CanChangePage);
        AddRowCommand = new RelayCommand(AddRow, CanWriteCommand);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, CanDeleteSelected);
        RejectChangesCommand = new RelayCommand(RejectProductChanges, CanRejectChanges);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        OpenCategoryManagerCommand = new AsyncRelayCommand(OpenCategoryManagerAsync, CanRunCommand);

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(ResetCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(PreviousPageCommand);
        RegisterBusyAwareCommand(NextPageCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectChangesCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(OpenCategoryManagerCommand);
    }

    public string Description { get; } = "查询、编辑和维护产品基础资料。";

    public BaseListModel<ProductLineModel, ProductDto> Lines { get; }

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string CategoryName
    {
        get => _categoryName;
        set => SetProperty(ref _categoryName, value);
    }

    public string MaterialType
    {
        get => _materialType;
        set => SetProperty(ref _materialType, value);
    }

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageStatus));
                NotifyPagingCommands();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            var normalized = value <= 0 ? DefaultPageSize : value;
            if (SetProperty(ref _pageSize, normalized))
            {
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageStatus));
                NotifyPagingCommands();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageStatus));
                NotifyPagingCommands();
            }
        }
    }

    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    public ProductLineModel? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
            {
                Lines.SelectedItem = value;
                DeleteSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public bool HasChanges => Lines.HasChanges;

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand PreviousPageCommand { get; }

    public IAsyncRelayCommand NextPageCommand { get; }

    public IAsyncRelayCommand<PageChangeRequest> PageChangeCommand { get; }

    public IRelayCommand AddRowCommand { get; }

    public IAsyncRelayCommand DeleteSelectedCommand { get; }

    public IRelayCommand RejectChangesCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand OpenCategoryManagerCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadPageAsync(confirmChanges: false);
    }

    private Task SearchAsync()
    {
        return SearchCoreAsync();
    }

    private async Task ResetAsync()
    {
        if (!await EnsureCanDiscardChangesAsync())
        {
            return;
        }

        Keyword = string.Empty;
        CategoryName = string.Empty;
        MaterialType = string.Empty;
        PageSize = DefaultPageSize;
        PageIndex = 1;
        await LoadPageAsync(confirmChanges: false);
    }

    private Task RefreshAsync()
    {
        return LoadPageAsync(confirmChanges: true);
    }

    private Task PreviousPageAsync()
    {
        return GoToPageAsync(Math.Max(1, PageIndex - 1));
    }

    private Task NextPageAsync()
    {
        return GoToPageAsync(Math.Min(TotalPages, PageIndex + 1));
    }

    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null)
        {
            return Task.CompletedTask;
        }

        return GoToPageAsync(request.NewIndex, request.NewSize);
    }

    private async Task SearchCoreAsync()
    {
        if (!await EnsureCanDiscardChangesAsync())
        {
            return;
        }

        PageIndex = 1;
        await LoadPageAsync(confirmChanges: false);
    }

    private Task GoToPageAsync(int targetPage)
    {
        return GoToPageAsync(targetPage, PageSize);
    }

    private async Task GoToPageAsync(int targetPage, int targetPageSize)
    {
        var normalizedPageSize = targetPageSize <= 0 ? DefaultPageSize : targetPageSize;
        var normalizedPageIndex = Math.Max(1, targetPage);

        if (normalizedPageIndex == PageIndex && normalizedPageSize == PageSize)
        {
            return;
        }

        var previousPage = PageIndex;
        var previousPageSize = PageSize;
        PageSize = normalizedPageSize;
        PageIndex = normalizedPageIndex;
        var loaded = await LoadPageAsync(confirmChanges: true);
        if (!loaded)
        {
            PageIndex = previousPage;
            PageSize = previousPageSize;
        }
    }

    private void AddRow()
    {
        var line = Lines.AddNew(new ProductDto
        {
            Uid = Guid.Empty,
            Sku = string.Empty,
            MasterSku = string.Empty,
            DescriptionEn = string.Empty,
            DescriptionZh = string.Empty,
            CategoryName = string.Empty,
            MaterialType = string.Empty,
            SupplyType = string.Empty,
            BomNo = string.Empty,
            Note = string.Empty,
        });

        SelectedLine = line;
        UpdateChangeState();
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedLine is null)
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "删除产品行",
            $"确定要删除产品 {SelectedLine.Sku} 吗？保存前可以撤销。",
            "删除");

        if (!confirmed)
        {
            return;
        }

        Lines.Remove(SelectedLine);
        SelectedLine = Lines.Items.FirstOrDefault();
        UpdateChangeState();
    }

    private void RejectProductChanges()
    {
        Lines.RejectChanges();
        SelectedLine = Lines.Items.FirstOrDefault();
        RejectChanges();
        UpdateChangeState();
    }

    private Task OpenCategoryManagerAsync()
    {
        return _categoryManagerDialogService.ShowAsync();
    }

    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var validationError = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                ValidationSummary = validationError;
                _messageService.ShowWarning("产品资料未保存", validationError);
                return;
            }

            var changeSet = Lines.GetChangeSet();
            if (!changeSet.HasChanges)
            {
                _messageService.ShowInfo("产品资料", "没有需要保存的变更。");
                return;
            }

            var request = new ProductBatchSaveRequest
            {
                CreateItems = changeSet.Added.ToList(),
                UpdateItems = changeSet.Modified.ToList(),
                DeleteItems = changeSet.Deleted
                    .Select(product => product.Uid)
                    .Where(uid => uid != Guid.Empty)
                    .Distinct()
                    .ToList(),
            };

            if (!request.HasChanges)
            {
                Lines.AcceptChanges();
                AcceptChanges();
                UpdateChangeState();
                _messageService.ShowInfo("产品资料", "新增行已移除，没有需要提交到服务端的变更。");
                return;
            }

            var result = await _apiCallRunner.RunAsync(
                () => _productApi.BatchSaveAsync(request),
                "保存产品资料失败");

            if (!result.Success)
            {
                ValidationSummary = result.Message;
                _messageService.ShowError("保存产品资料失败", result.Message);
                return;
            }

            Lines.AcceptChanges();
            AcceptChanges();
            UpdateChangeState();

            var affectedRows = result.Data?.AffectedRowsTotal ?? 0;
            _messageService.ShowSuccess("产品资料已保存", $"已提交 {affectedRows} 条变更。");
            await LoadPageCoreAsync();
        }, "正在保存产品资料...");
    }

    private async Task<bool> LoadPageAsync(bool confirmChanges)
    {
        if (confirmChanges && !await EnsureCanDiscardChangesAsync())
        {
            return false;
        }

        var loaded = false;
        await RunBusyAsync(async () =>
        {
            loaded = await LoadPageCoreAsync();
        }, "正在加载产品资料...");

        return loaded;
    }

    private async Task<bool> LoadPageCoreAsync()
    {
        var request = new ProductSearchRequest
        {
            Keyword = NullIfWhiteSpace(Keyword),
            CategoryName = NullIfWhiteSpace(CategoryName),
            MaterialType = NullIfWhiteSpace(MaterialType),
            PageIndex = PageIndex,
            PageSize = PageSize,
        };

        var result = await _apiCallRunner.RunAsync(
            () => _productApi.GetPagedAsync(request),
            "加载产品资料失败");

        if (!result.Success)
        {
            ValidationSummary = result.Message;
            ResultSummary = "产品资料加载失败。";
            _messageService.ShowError("加载产品资料失败", result.Message);
            return false;
        }

        var page = result.Data;
        Lines.LoadFromDtos(page?.Items ?? []);
        SelectedLine = Lines.Items.FirstOrDefault();
        TotalCount = page?.TotalCount ?? 0;
        PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
        PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
        ResultSummary = $"共 {TotalCount} 条，本页 {Lines.Items.Count} 条。";
        AcceptChanges();
        UpdateChangeState();

        return true;
    }

    private async Task<bool> EnsureCanDiscardChangesAsync()
    {
        if (!Lines.HasChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃未保存变更",
            "当前产品列表有未保存的修改。继续操作会放弃这些修改。",
            "继续");
    }

    private bool CanRunCommand()
    {
        return IsNotBusy;
    }

    private bool CanWriteCommand()
    {
        return IsNotBusy && CanWrite;
    }

    private bool CanDeleteSelected()
    {
        return IsNotBusy && CanWrite && SelectedLine is not null;
    }

    private bool CanRejectChanges()
    {
        return IsNotBusy && Lines.HasChanges;
    }

    private bool CanSave()
    {
        return IsNotBusy && CanWrite && Lines.HasChanges;
    }

    private bool CanGoPreviousPage()
    {
        return IsNotBusy && PageIndex > 1;
    }

    private bool CanGoNextPage()
    {
        return IsNotBusy && PageIndex < TotalPages;
    }

    private bool CanChangePage(PageChangeRequest? request)
    {
        return IsNotBusy && request is not null;
    }

    private void OnLinesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BaseListModel<ProductLineModel, ProductDto>.HasChanges)
            or nameof(BaseListModel<ProductLineModel, ProductDto>.CheckedCount))
        {
            UpdateChangeState();
        }
    }

    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RejectChangesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPagingCommands()
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
