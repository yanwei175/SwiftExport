using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Controls;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Colors.ViewModels;

public sealed class ProductColorsViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private const int SearchDebounceMilliseconds = 500;
    private readonly IProductColorApi _colorApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private bool _isInitialized;
    private CancellationTokenSource? _searchDebounceCts;
    private string _keyword = string.Empty;
    private int _pageIndex = 1;
    private int _pageSize = DefaultPageSize;
    private int _totalCount;
    private ProductColorLineModel? _selectedLine;
    private string _resultSummary = "尚未加载产品颜色。";

    public ProductColorsViewModel(
        IProductColorApi colorApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _colorApi = colorApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;

        Title = "产品颜色管理";
        Lines = new BaseListModel<ProductColorLineModel, ProductColorDto>(_ => new ProductColorLineModel());
        Lines.PropertyChanged += OnLinesPropertyChanged;

        SearchCommand = new AsyncRelayCommand(SearchImmediatelyAsync, CanRunCommand);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRunCommand);
        PageChangeCommand = new AsyncRelayCommand<PageChangeRequest>(PageChangeAsync, CanChangePage);
        AddRowCommand = new RelayCommand(AddRow, CanWriteCommand);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, CanDeleteSelected);
        RejectChangesCommand = new RelayCommand(RejectColorChanges, CanRejectChanges);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        MoreCommand = new RelayCommand<string?>(ShowPlaceholderAction);

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectChangesCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(MoreCommand);
    }

    public string Description { get; } = "维护产品颜色代码、英文名称和中文名称。";

    public BaseListModel<ProductColorLineModel, ProductColorDto> Lines { get; }

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value) && _isInitialized)
            {
                ScheduleDebouncedSearch();
            }
        }
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

    public ProductColorLineModel? SelectedLine
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

    public bool IsEmpty => IsNotBusy && Lines.Items.Count == 0;

    public IAsyncRelayCommand SearchCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<PageChangeRequest> PageChangeCommand { get; }

    public IRelayCommand AddRowCommand { get; }

    public IAsyncRelayCommand DeleteSelectedCommand { get; }

    public IRelayCommand RejectChangesCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand<string?> MoreCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadPageAsync(confirmChanges: false);
    }

    private async Task SearchImmediatelyAsync()
    {
        CancelPendingSearch();
        await SearchCoreAsync();
    }

    private Task RefreshAsync()
    {
        CancelPendingSearch();
        return LoadPageAsync(confirmChanges: true);
    }

    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null)
        {
            return Task.CompletedTask;
        }

        CancelPendingSearch();
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
        var line = Lines.AddNew(new ProductColorDto
        {
            Uid = Guid.Empty,
            ColorCodeUnique = string.Empty,
            ColorCode = string.Empty,
            ColorNameEn = string.Empty,
            ColorNameZh = string.Empty,
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

        var displayCode = string.IsNullOrWhiteSpace(SelectedLine.ColorCodeUnique)
            ? SelectedLine.ColorCode
            : SelectedLine.ColorCodeUnique;
        var confirmed = await _dialogService.ConfirmAsync(
            "删除产品颜色",
            $"确定要删除颜色 {displayCode} 吗？保存前可以撤销。",
            "删除");

        if (!confirmed)
        {
            return;
        }

        Lines.Remove(SelectedLine);
        SelectedLine = Lines.Items.FirstOrDefault();
        UpdateChangeState();
    }

    private void RejectColorChanges()
    {
        Lines.RejectChanges();
        SelectedLine = Lines.Items.FirstOrDefault();
        RejectChanges();
        UpdateChangeState();
    }

    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var validationError = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                ValidationSummary = validationError;
                _messageService.ShowWarning("产品颜色未保存", validationError);
                return;
            }

            var changeSet = Lines.GetChangeSet();
            if (!changeSet.HasChanges)
            {
                _messageService.ShowInfo("产品颜色", "没有需要保存的变更。");
                return;
            }

            var request = new ProductColorBatchSaveRequest
            {
                CreateItems = changeSet.Added.ToList(),
                UpdateItems = changeSet.Modified.ToList(),
                DeleteItems = changeSet.Deleted
                    .Select(color => color.Uid)
                    .Where(uid => uid != Guid.Empty)
                    .Distinct()
                    .ToList(),
            };

            if (!request.HasChanges)
            {
                Lines.AcceptChanges();
                AcceptChanges();
                UpdateChangeState();
                _messageService.ShowInfo("产品颜色", "新增行已移除，没有需要提交到服务端的变更。");
                return;
            }

            var result = await _apiCallRunner.RunAsync(
                () => _colorApi.BatchSaveAsync(request),
                "保存产品颜色失败");

            if (!result.Success)
            {
                ValidationSummary = result.Message;
                _messageService.ShowError("保存产品颜色失败", result.Message);
                return;
            }

            Lines.AcceptChanges();
            AcceptChanges();
            UpdateChangeState();

            var affectedRows = result.Data?.AffectedRowsTotal ?? 0;
            _messageService.ShowSuccess("产品颜色已保存", $"已提交 {affectedRows} 条变更。");
            await LoadPageCoreAsync();
        }, "正在保存产品颜色...");
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
        }, "正在加载产品颜色...");

        return loaded;
    }

    private async Task<bool> LoadPageCoreAsync()
    {
        var keyword = NullIfWhiteSpace(Keyword);
        var request = new ProductColorSearchRequest
        {
            Keyword = keyword,
            PageIndex = PageIndex,
            PageSize = PageSize,
            BatchKeywords = keyword is null ? null : [keyword],
        };

        var result = await _apiCallRunner.RunAsync(
            () => _colorApi.GetPagedAsync(request),
            "加载产品颜色失败");

        if (!result.Success)
        {
            ValidationSummary = result.Message;
            ResultSummary = "产品颜色加载失败。";
            _messageService.ShowError("加载产品颜色失败", result.Message);
            OnPropertyChanged(nameof(IsEmpty));
            return false;
        }

        var page = result.Data;
        Lines.LoadFromDtos(page?.Items ?? []);
        SelectedLine = Lines.Items.FirstOrDefault();
        TotalCount = page?.TotalCount ?? 0;
        PageIndex = page?.PageIndex > 0 ? page.PageIndex : PageIndex;
        PageSize = page?.PageSize > 0 ? page.PageSize : PageSize;
        ResultSummary = $"共 {TotalCount} 条，本页 {Lines.Items.Count} 条。";
        ValidationSummary = string.Empty;
        AcceptChanges();
        UpdateChangeState();
        OnPropertyChanged(nameof(IsEmpty));

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
            "当前产品颜色列表有未保存的修改。继续操作会放弃这些修改。",
            "继续");
    }

    private void ScheduleDebouncedSearch()
    {
        CancelPendingSearch();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedSearchAsync(cts.Token);
    }

    private async Task DebouncedSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                await SearchCoreAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingSearch()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;
    }

    private void ShowPlaceholderAction(string? actionName)
    {
        var name = string.IsNullOrWhiteSpace(actionName) ? "该功能" : actionName;
        _messageService.ShowInfo("产品颜色管理", $"{name}将在后续阶段接入。");
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

    private bool CanChangePage(PageChangeRequest? request)
    {
        return IsNotBusy && request is not null;
    }

    private void OnLinesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BaseListModel<ProductColorLineModel, ProductColorDto>.HasChanges)
            or nameof(BaseListModel<ProductColorLineModel, ProductColorDto>.CheckedCount))
        {
            UpdateChangeState();
        }
    }

    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsEmpty));
        SaveCommand.NotifyCanExecuteChanged();
        RejectChangesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPagingCommands()
    {
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
