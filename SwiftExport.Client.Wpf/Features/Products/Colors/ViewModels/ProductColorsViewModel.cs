using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Controls;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Colors.ViewModels;

public sealed partial class ProductColorsViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private const int SearchDebounceMilliseconds = 500;
    private readonly IProductColorApi _colorApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private bool _isInitialized;
    private CancellationTokenSource? _searchDebounceCts;
    [ObservableProperty]
    private string keyword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    private int pageIndex = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    private int pageSize = DefaultPageSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    private int totalCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private ProductColorLineModel? selectedLine;

    [ObservableProperty]
    private string resultSummary = "尚未加载产品颜色。";

    [ObservableProperty]
    private bool hasValidationError;

    [ObservableProperty]
    private bool isCellSelectionMode;

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

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectAllChangesCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(MoreCommand);
    }

    public string Description { get; } = "维护产品颜色代码、英文名称和中文名称。";

    public BaseListModel<ProductColorLineModel, ProductColorDto> Lines { get; }

    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    public bool HasChanges => Lines.HasChanges;

    public bool IsEmpty => Lines.Items.Count == 0;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await LoadPageAsync(confirmChanges: false);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task SearchAsync()
    {
        CancelPendingSearch();
        await SearchCoreAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private Task RefreshAsync()
    {
        CancelPendingSearch();
        return LoadPageAsync(confirmChanges: true);
    }

    [RelayCommand(CanExecute = nameof(CanChangePage))]
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

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
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

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var line = SelectedLine;
        if (line is null)
        {
            return;
        }

        var displayCode = string.IsNullOrWhiteSpace(line.ColorCodeUnique)
            ? line.ColorCode
            : line.ColorCodeUnique;
        var confirmed = await _dialogService.ConfirmAsync(
            "删除产品颜色",
            $"确定要删除颜色 {displayCode} 吗？保存前可以撤销。",
            "删除");

        if (!confirmed)
        {
            return;
        }

        Lines.Remove(line);
        SelectedLine = Lines.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanRejectChanges))]
    private void RejectAllChanges()
    {
        Lines.RejectChanges();
        SelectedLine = Lines.Items.FirstOrDefault();
        RejectChanges();
        SetValidationFeedback(null);
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var validationError = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetValidationFeedback(validationError);
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
                CreateItems = changeSet.Added
                    .Select(color => new CreateItemRequest<ProductColorDto> { Data = color })
                    .ToList(),
                UpdateItems = changeSet.Modified
                    .Select(color => new UpdateItemRequest<ProductColorDto>
                    {
                        Uid = color.Uid,
                        Version = color.Version,
                        Data = color
                    })
                    .ToList(),
                DeleteItems = changeSet.Deleted
                    .Where(color => color.Uid != Guid.Empty)
                    .Select(color => new DeleteItemRequest
                    {
                        Uid = color.Uid,
                        Version = color.Version,
                        IsSoftDelete = true
                    })
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
                SetValidationFeedback(result.Message);
                _messageService.ShowError("保存产品颜色失败", result.Message);
                return;
            }

            Lines.AcceptChanges();
            AcceptChanges();
            SetValidationFeedback(null);
            UpdateChangeState();

            var affectedRows = result.Data?.SuccessCount ?? 0;
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
        OnPropertyChanged(nameof(IsEmpty));

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
            SetValidationFeedback(result.Message);
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
        SetValidationFeedback(null);
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

    [RelayCommand]
    private void More(string? actionName)
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
        RejectAllChangesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPagingCommands()
    {
        PageChangeCommand.NotifyCanExecuteChanged();
    }

    partial void OnKeywordChanged(string value)
    {
        if (_isInitialized)
        {
            ScheduleDebouncedSearch();
        }
    }

    partial void OnPageIndexChanged(int value) => NotifyPagingCommands();

    partial void OnPageSizeChanged(int value) => NotifyPagingCommands();

    partial void OnTotalCountChanged(int value) => NotifyPagingCommands();

    partial void OnSelectedLineChanged(ProductColorLineModel? value)
    {
        Lines.SelectedItem = value;
    }

    protected override async Task HandleExceptionAsync(Exception exception)
    {
        await base.HandleExceptionAsync(exception);
        HasValidationError = true;
        _messageService.ShowError("产品颜色操作失败", exception.Message);
    }

    private void SetValidationFeedback(string? message)
    {
        ValidationSummary = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        HasValidationError = ValidationSummary is not null;
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
