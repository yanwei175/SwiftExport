using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Categories.ViewModels;

/// <summary>管理分类页的主型号快速归类查询、勾选和批量命令。</summary>
public sealed partial class ProductCategoryAssignmentViewModel : ViewModelBaseEx
{
    private const int DefaultPageSize = 50;
    private readonly IProductMasterApi _productMasterApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;

    public ProductCategoryAssignmentViewModel(
        IProductMasterApi productMasterApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _productMasterApi = productMasterApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;

        Title = "主型号快速归类";
        RegisterBusyAwareCommand(OpenAssignmentPanelCommand);
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(AssignSelectedCommand);
    }

    public ObservableCollection<ProductCategoryAssignmentLineModel> Lines { get; } = [];

    public IReadOnlyList<int> PageSizeOptions { get; } = [20, 50, 100, 200];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetCategoryName))]
    [NotifyCanExecuteChangedFor(nameof(OpenAssignmentPanelCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignSelectedCommand))]
    public partial ProductCategoryTreeItemModel? TargetCategory { get; private set; }

    [ObservableProperty]
    public partial bool IsAssignmentPanelOpen { get; private set; }

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
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
    public partial string StatusMessage { get; private set; } = "打开后才会加载主型号。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignSelectedCommand))]
    public partial bool HostHasChanges { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignSelectedCommand))]
    public partial bool HostCanWrite { get; private set; } = true;

    public string TargetCategoryName => TargetCategory?.CategoryName ?? "未选择分类";

    public int CheckedCount => Lines.Count(line => line.IsChecked);

    public bool HasRows => Lines.Count > 0;

    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    public bool IsAllChecked
    {
        get => Lines.Count > 0 && Lines.All(line => line.IsChecked);
        set
        {
            foreach (var line in Lines)
            {
                line.IsChecked = value;
            }

            NotifySelectionChanged();
        }
    }

    public void SetHostState(
        ProductCategoryTreeItemModel? category,
        bool hasCategoryChanges,
        bool canWrite)
    {
        var targetChanged = TargetCategory?.Uid != category?.Uid;
        TargetCategory = category;
        HostHasChanges = hasCategoryChanges;
        HostCanWrite = canWrite;
        OnPropertyChanged(nameof(TargetCategoryName));

        if (targetChanged)
        {
            CancelPendingLoad();
            ClearLines();
            PageIndex = 1;
            TotalCount = 0;
            StatusMessage = category is null
                ? "请选择一个分类。"
                : category.State == ProductCategoryNodeState.Added
                    ? "请先保存新增分类，再进行主型号归类。"
                    : "目标分类已切换。";
        }
        else if (IsAssignmentPanelOpen && hasCategoryChanges)
        {
            StatusMessage = "请先保存分类结构，再执行主型号归类。";
        }
        else if (IsAssignmentPanelOpen && !canWrite)
        {
            StatusMessage = "当前页面为只读状态，不能执行主型号归类。";
        }
        else if (IsAssignmentPanelOpen && Lines.Count > 0)
        {
            StatusMessage = $"共 {TotalCount} 个待归类主型号，本页 {Lines.Count} 个。";
        }

        OpenAssignmentPanelCommand.NotifyCanExecuteChanged();
        AssignSelectedCommand.NotifyCanExecuteChanged();
    }

    public async Task ChangeTargetAsync(
        ProductCategoryTreeItemModel? category,
        bool hasCategoryChanges,
        bool canWrite)
    {
        SetHostState(category, hasCategoryChanges, canWrite);
        if (IsAssignmentPanelOpen && CanLoadTarget())
        {
            await LoadPageAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenAssignmentPanel))]
    private async Task OpenAssignmentPanelAsync()
    {
        IsAssignmentPanelOpen = true;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private void CloseAssignmentPanel()
    {
        CancelPendingLoad();
        IsAssignmentPanelOpen = false;
        ClearLines();
        TotalCount = 0;
        PageIndex = 1;
        StatusMessage = "快速归类已关闭，主型号数据已释放。";
    }

    [RelayCommand(CanExecute = nameof(CanRunQuery))]
    private async Task SearchAsync()
    {
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRunQuery))]
    private Task RefreshAsync() => LoadPageAsync();

    [RelayCommand(CanExecute = nameof(CanChangePage))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null)
        {
            return;
        }

        PageIndex = Math.Max(1, request.NewIndex);
        PageSize = Math.Clamp(request.NewSize <= 0 ? DefaultPageSize : request.NewSize, 1, 200);
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAssignSelected))]
    private async Task AssignSelectedAsync()
    {
        var target = TargetCategory;
        var checkedLines = Lines.Where(line => line.IsChecked).ToArray();
        if (target is null || checkedLines.Length == 0 || !CanAssignSelected())
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "批量归类主型号",
            $"将 {checkedLines.Length} 个主型号归入“{target.CategoryName}”，是否继续？",
            "确认归类");
        if (!confirmed)
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var request = new AssignProductMastersToCategoryRequest
            {
                TargetCategoryId = target.Uid,
                Items = checkedLines.Select(line => new AssignProductMasterToCategoryItem
                {
                    ProductMasterId = line.Uid,
                    ExpectedVersion = line.Version
                }).ToList()
            };

            var result = await _apiCallRunner.RunAsync(
                () => _productMasterApi.AssignCategoryAsync(request),
                "主型号归类失败");
            if (!result.Success)
            {
                ValidationSummary = result.Message;
                StatusMessage = result.Message;
                _messageService.ShowError("主型号归类失败", result.Message);
                return;
            }

            var updatedCount = result.Data?.UpdatedCount ?? 0;
            _messageService.ShowSuccess("主型号归类完成", $"已归类 {updatedCount} 个主型号。");
            ValidationSummary = null;
            await LoadPageAsync();

            if (Lines.Count == 0 && PageIndex > TotalPages)
            {
                PageIndex = TotalPages;
                await LoadPageAsync();
            }
        }, "正在批量归类主型号...");
    }

    private async Task LoadPageAsync()
    {
        var target = TargetCategory;
        if (!IsAssignmentPanelOpen || target is null || target.State == ProductCategoryNodeState.Added)
        {
            return;
        }

        CancelPendingLoad();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var generation = Interlocked.Increment(ref _loadGeneration);
        var targetId = target.Uid;

        await RunBusyAsync(async () =>
        {
            var request = new ProductMasterSearchRequest
            {
                ExcludeCategoryId = targetId,
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize
            };
            var result = await _apiCallRunner.RunAsync(
                () => _productMasterApi.GetPagedAsync(request, cts.Token),
                cts.Token,
                "加载待归类主型号失败");

            if (cts.IsCancellationRequested
                || generation != Volatile.Read(ref _loadGeneration)
                || TargetCategory?.Uid != targetId
                || !IsAssignmentPanelOpen)
            {
                return;
            }

            if (!result.Success)
            {
                ValidationSummary = result.Message;
                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "主型号查询已取消。"
                    : result.Message;
                return;
            }

            ReplaceLines(result.Data?.Items?.Select(ProductCategoryAssignmentLineModel.FromDto) ?? []);
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
            StatusMessage = $"共 {TotalCount} 个待归类主型号，本页 {Lines.Count} 个。";
            ValidationSummary = null;
        }, "正在加载待归类主型号...", skipIfBusy: false);
    }

    private void ReplaceLines(IEnumerable<ProductCategoryAssignmentLineModel> lines)
    {
        ClearLines();
        foreach (var line in lines)
        {
            line.PropertyChanged += OnLinePropertyChanged;
            Lines.Add(line);
        }

        NotifySelectionChanged();
        OnPropertyChanged(nameof(HasRows));
    }

    private void ClearLines()
    {
        foreach (var line in Lines)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
        }

        Lines.Clear();
        NotifySelectionChanged();
        OnPropertyChanged(nameof(HasRows));
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductCategoryAssignmentLineModel.IsChecked))
        {
            NotifySelectionChanged();
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(IsAllChecked));
        AssignSelectedCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingLoad()
    {
        Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private bool CanLoadTarget() =>
        TargetCategory is { State: not ProductCategoryNodeState.Added };

    private bool CanOpenAssignmentPanel() =>
        IsNotBusy && !IsAssignmentPanelOpen && CanLoadTarget();

    private bool CanRunQuery() =>
        IsNotBusy && IsAssignmentPanelOpen && CanLoadTarget();

    private bool CanChangePage(PageChangeRequest? request) =>
        CanRunQuery() && request is not null;

    private bool CanAssignSelected() =>
        IsNotBusy
        && IsAssignmentPanelOpen
        && CanLoadTarget()
        && HostCanWrite
        && !HostHasChanges
        && CheckedCount > 0;
}
