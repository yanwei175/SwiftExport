using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

public sealed partial class GrossDemandWorkspaceViewModel : ViewModelBaseEx,
    IAsyncInitializable,
    IDocumentTabViewModel,
    IDisposable
{
    public const string RouteKey = "gross-demands";

    private readonly IGrossDemandWorkspaceApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _initialized;
    private bool _syncingChecks;
    private bool _disposed;
    private Guid? _appliedSourceUid;

    public GrossDemandWorkspaceViewModel(
        IGrossDemandWorkspaceApi api,
        IApiCallRunner apiCallRunner,
        INavigationService navigationService,
        IDialogService dialogService,
        IMessageService messageService,
        GrossDemandManualEditorViewModel manualEditor,
        GrossDemandTraceViewModel trace)
    {
        _api = api;
        _apiCallRunner = apiCallRunner;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _messageService = messageService;
        ManualEditor = manualEditor;
        Trace = trace;
        Title = "毛需求";
        ManualEditor.PropertyChanged += OnManualEditorPropertyChanged;
        ManualEditor.Saved += OnManualSaved;

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(DecomposeSelectedCommand);
        RegisterBusyAwareCommand(RollbackSelectedCommand);
        RegisterBusyAwareCommand(CreateManualCommand);
        RegisterBusyAwareCommand(EditSelectedManualCommand);
        RegisterBusyAwareCommand(CloseSelectedManualCommand);
        RegisterBusyAwareCommand(OpenSourceCommand);
        RegisterBusyAwareCommand(OpenTraceCommand);
        RegisterBusyAwareCommand(ToggleTraceCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(SelectSourceGroupCommand);
        SourceGroups.Add(GrossDemandSourceGroupModel.CreateAll(0));
        SelectedSourceGroup = SourceGroups[0];
    }

    public GrossDemandManualEditorViewModel ManualEditor { get; }
    public GrossDemandTraceViewModel Trace { get; }
    public ObservableCollection<GrossDemandRowModel> Items { get; } = [];
    public ObservableCollection<GrossDemandSourceGroupModel> SourceGroups { get; } = [];

    public IReadOnlyList<GrossDemandFilterOption<string>> SourceTypeOptions { get; } =
    [
        new("SO", "销售订单"),
        new("MD", "手工需求"),
        new("CR", "投诉补货"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> LifecycleStatusOptions { get; } =
    [
        new(GrossDemandLifecycleStatusEnum.Active, "活动"),
        new(GrossDemandLifecycleStatusEnum.Closed, "已关闭"),
        new(GrossDemandLifecycleStatusEnum.Voided, "已作废"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<GrossDemandDecompositionStatusEnum>> DecompositionStatusOptions { get; } =
    [
        new(GrossDemandDecompositionStatusEnum.Pending, "待拆解"),
        new(GrossDemandDecompositionStatusEnum.Decomposed, "已拆解"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<DemandExecutionProgressEnum>> ExecutionProgressOptions { get; } =
    [
        new(DemandExecutionProgressEnum.NotStarted, "未开始"),
        new(DemandExecutionProgressEnum.PartiallyAllocated, "部分分配"),
        new(DemandExecutionProgressEnum.FullyAllocated, "全部分配"),
        new(DemandExecutionProgressEnum.PartiallyCompleted, "部分完成"),
        new(DemandExecutionProgressEnum.Completed, "已完成"),
    ];

    public IReadOnlyList<GrossDemandFilterOption<bool?>> StandardTypeOptions { get; } =
    [
        new(null, "全部类型"),
        new(false, "标准件"),
        new(true, "非标件"),
    ];

    public ObservableCollection<GrossDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandLifecycleStatusEnum>> SelectedLifecycleStatuses { get; } =
        [
        new(GrossDemandLifecycleStatusEnum.Active, "活动")

        ];
    public ObservableCollection<GrossDemandFilterOption<GrossDemandDecompositionStatusEnum>> SelectedDecompositionStatuses { get; } = [];
    public ObservableCollection<GrossDemandFilterOption<DemandExecutionProgressEnum>> SelectedExecutionProgresses { get; } =
        [

        ];

    public string DocumentTitle => "毛需求";
    public bool HasUnsavedChanges => ManualEditor.HasUnsavedChanges;
    public int TotalPages => IsSourceSelected || TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => IsSourceSelected ? $"当前来源共 {TotalCount} 条" : $"第 {PageIndex} / {TotalPages} 页";
    public int CheckedCount => Items.Count(item => item.IsChecked);
    public bool IsTraceOpen => IsDrawerOpen && ReferenceEquals(ActiveDrawerContent, Trace);
    public bool IsSourceSelected => SelectedSourceGroup?.SourceUid.HasValue == true;

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceNosText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SkusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerSkusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMultiLineFilterOpen { get; set; }

    [ObservableProperty]
    public partial DateTime? RequiredDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? RequiredDateTo { get; set; }

    [ObservableProperty]
    public partial GrossDemandFilterOption<bool?>? SelectedStandardType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceSelected))]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    [NotifyCanExecuteChangedFor(nameof(PageChangeCommand))]
    public partial GrossDemandSourceGroupModel? SelectedSourceGroup { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedManualCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTraceCommand))]
    public partial GrossDemandRowModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTraceOpen))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTraceCommand))]
    public partial object? ActiveDrawerContent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTraceOpen))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTraceCommand))]
    public partial bool IsDrawerOpen { get; private set; }

    [ObservableProperty]
    public partial bool? IsAllChecked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageSize { get; set; } = 20;

    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载毛需求。";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SelectedStandardType = StandardTypeOptions[0];
        await LoadPageAsync(refreshSourceGroups: true);
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        return await ConfirmDiscardManualChangesAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        if (query is not null)
        {
            Keyword = query;
        }

        PageIndex = 1;
        await LoadPageAsync(refreshSourceGroups: true);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (await ConfirmDiscardManualChangesAsync())
        {
            await LoadPageAsync(refreshSourceGroups: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        Keyword = string.Empty;
        SourceNosText = string.Empty;
        SkusText = string.Empty;
        CustomerSkusText = string.Empty;
        RequiredDateFrom = null;
        RequiredDateTo = null;
        SelectedStandardType = StandardTypeOptions[0];
        SelectedSourceTypes.Clear();
        SelectedLifecycleStatuses.Clear();
        SelectedDecompositionStatuses.Clear();
        SelectedExecutionProgresses.Clear();
        _appliedSourceUid = null;
        PageIndex = 1;
        await LoadPageAsync(refreshSourceGroups: true);
    }

    [RelayCommand(CanExecute = nameof(CanDecomposeSelected))]
    private async Task DecomposeSelectedAsync()
    {
        var selected = Items.Where(item => item.IsChecked).ToArray();
        if (selected.Length == 0
            || !await ConfirmDiscardManualChangesAsync()
            || !await _dialogService.ConfirmAsync(
                "拆解毛需求",
                $"将按当前产品和 BOM 规则拆解 {selected.Length} 条毛需求。是否继续？",
                "拆解"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.DecomposeAsync(new GrossDemandDecomposeCommand
                {
                    Items = CreateCommandItems(selected),
                }),
                "拆解毛需求失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("拆解失败", result.Message);
                return;
            }

            ShowCommandSummary("毛需求拆解", result.Data.Items.Select(item => (item.Outcome, item.Message)));
            await LoadPageAsync(refreshSourceGroups: true);
        }, "正在拆解毛需求...");
    }

    [RelayCommand(CanExecute = nameof(CanRollbackSelected))]
    private async Task RollbackSelectedAsync()
    {
        var selected = Items.Where(item => item.IsChecked).ToArray();
        if (selected.Length == 0
            || !await ConfirmDiscardManualChangesAsync()
            || !await _dialogService.ConfirmAsync(
                "安全回退拆解",
                $"将回退 {selected.Length} 条毛需求的当前拆解版本。存在活动下游执行单时服务端会阻断。是否继续？",
                "安全回退"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.RollbackAsync(new GrossDemandRollbackCommand
                {
                    Items = CreateCommandItems(selected),
                    Reason = "用户从毛需求工作台安全回退拆解",
                }),
                "回退毛需求拆解失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("回退失败", result.Message);
                return;
            }

            ShowCommandSummary("拆解回退", result.Data.Items.Select(item => (item.Outcome, item.Message)));
            await LoadPageAsync(refreshSourceGroups: true);
        }, "正在安全回退拆解...");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CreateManualAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        Trace.Clear();
        ManualEditor.OpenCreate();
        ActiveDrawerContent = ManualEditor;
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedManual))]
    private async Task EditSelectedManualAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.GetDetailAsync(selected.Uid, cancellation.Token),
                cancellation.Token,
                "加载手工毛需求失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cancellation))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("无法编辑手工毛需求", result.Message);
                return;
            }

            Trace.Clear();
            ManualEditor.OpenEdit(result.Data);
            ActiveDrawerContent = ManualEditor;
            IsDrawerOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_detailCancellation, cancellation))
            {
                _detailCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseSelectedManual))]
    private async Task CloseSelectedManualAsync()
    {
        var selected = SelectedItem;
        if (selected is null
            || !await ConfirmDiscardManualChangesAsync()
            || !await _dialogService.ConfirmAsync(
                "关闭手工毛需求",
                $"关闭 {selected.SourceNo} 后将保留历史且不能继续拆解。是否继续？",
                "关闭"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.CloseManualAsync(selected.Uid, new CloseGrossDemandCommand
                {
                    Uid = selected.Uid,
                    Version = selected.Version,
                    Reason = "用户从毛需求工作台关闭手工需求",
                }),
                "关闭手工毛需求失败");
            if (!result.Success)
            {
                _messageService.ShowError("关闭失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("手工毛需求已关闭", selected.SourceNo);
            await LoadPageAsync(refreshSourceGroups: true);
        }, "正在关闭手工毛需求...");
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task OpenSourceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        if (string.Equals(selected.SourceType, "SO", StringComparison.OrdinalIgnoreCase))
        {
            await _navigationService.OpenDocumentAsync(
                SalesOrderManagerViewModel.DetailRouteKey,
                $"sales-order:{selected.SourceUid:N}",
                new SalesOrderDocumentOpenRequest(selected.SourceUid));
            return;
        }

        if (string.Equals(selected.SourceType, "MD", StringComparison.OrdinalIgnoreCase))
        {
            if (CanEditSelectedManual())
            {
                await EditSelectedManualAsync();
            }
            else
            {
                await OpenTraceAsync();
            }

            return;
        }

        _messageService.ShowInfo("来源追溯", "投诉补货来源页面尚未接入，请在需求追溯中查看来源快照。马上为你打开追溯。 ");
        await OpenTraceAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task OpenTraceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        ManualEditor.Close(discardChanges: true);
        ActiveDrawerContent = Trace;
        IsDrawerOpen = true;
        if (!await Trace.LoadAsync(selected.Uid))
        {
            _messageService.ShowWarning("需求追溯", Trace.StatusText);
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleTrace))]
    private async Task ToggleTraceAsync()
    {
        if (IsTraceOpen)
        {
            await CloseDrawerAsync();
            return;
        }

        await OpenTraceAsync();
    }

    [RelayCommand]
    private async Task CloseDrawerAsync()
    {
        if (!await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        ManualEditor.Close(discardChanges: true);
        Trace.Clear();
        ActiveDrawerContent = null;
        IsDrawerOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanPageChange))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await ConfirmDiscardManualChangesAsync())
        {
            return;
        }

        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSelectSourceGroup))]
    private async Task SelectSourceGroupAsync(GrossDemandSourceGroupModel? group)
    {
        if (group is null)
        {
            RestoreAppliedSourceSelection();
            return;
        }

        if (group.SourceUid == _appliedSourceUid)
        {
            return;
        }

        if (!await ConfirmDiscardManualChangesAsync())
        {
            RestoreAppliedSourceSelection();
            return;
        }

        _appliedSourceUid = group.SourceUid;
        SelectedSourceGroup = group;
        PageIndex = 1;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync(bool refreshSourceGroups = false)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var selectedUid = SelectedItem?.Uid;

        try
        {
            await RunBusyAsync(async () =>
            {
                if (refreshSourceGroups && !await RefreshSourceGroupsAsync(cancellation.Token))
                {
                    return;
                }

                var result = await _apiCallRunner.RunAsync(
                    () => _api.SearchAsync(CreateSearchRequest(), cancellation.Token),
                    cancellation.Token,
                    "查询毛需求失败");
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation))
                {
                    return;
                }

                if (!result.Success || result.Data is null)
                {
                    ResultSummary = result.Message;
                    return;
                }

                DetachRows();
                Items.Clear();
                foreach (var dto in result.Data.Items)
                {
                    var row = GrossDemandRowModel.FromDto(dto);
                    row.PropertyChanged += OnRowPropertyChanged;
                    Items.Add(row);
                }

                TotalCount = result.Data.TotalCount;
                PageIndex = IsSourceSelected ? 1 : result.Data.PageIndex <= 0 ? PageIndex : result.Data.PageIndex;
                if (!IsSourceSelected && result.Data.PageSize > 0)
                {
                    PageSize = result.Data.PageSize;
                }
                SelectedItem = selectedUid is null
                    ? Items.FirstOrDefault()
                    : Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Items.FirstOrDefault();
                ClearChecks();
                ResultSummary = IsSourceSelected
                    ? $"当前来源共 {TotalCount} 条毛需求，已全部加载。"
                    : $"共 {TotalCount} 条毛需求，本页 {Items.Count} 条。";
            }, "正在查询毛需求...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private GrossDemandSearchRequest CreateSearchRequest() => new()
    {
        Keyword = Keyword.Trim(),
        PageIndex = PageIndex,
        PageSize = PageSize,
        SourceTypes = SelectedSourceTypes.Select(item => item.Value).ToList(),
        SourceNos = SplitLines(SourceNosText),
        Skus = SplitLines(SkusText),
        CustomerSkus = SplitLines(CustomerSkusText),
        LifecycleStatuses = SelectedLifecycleStatuses.Select(item => item.Value).ToList(),
        DecompositionStatuses = SelectedDecompositionStatuses.Select(item => item.Value).ToList(),
        ExecutionProgresses = SelectedExecutionProgresses.Select(item => item.Value).ToList(),
        RequiredDateFrom = RequiredDateFrom,
        RequiredDateTo = RequiredDateTo,
        IsNonStandard = SelectedStandardType?.Value,
        SourceUid = _appliedSourceUid,
    };

    private async Task<bool> RefreshSourceGroupsAsync(CancellationToken cancellationToken)
    {
        var request = CreateSearchRequest();
        request.SourceUid = null;
        var result = await _apiCallRunner.RunAsync(
            () => _api.SearchSourceGroupsAsync(request, cancellationToken),
            cancellationToken,
            "查询毛需求来源分组失败");
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (!result.Success || result.Data is null)
        {
            ResultSummary = result.Message;
            return false;
        }

        var groups = result.Data.Select(GrossDemandSourceGroupModel.FromDto).ToArray();
        SourceGroups.Clear();
        SourceGroups.Add(GrossDemandSourceGroupModel.CreateAll(result.Data.Sum(item => item.DemandCount)));
        foreach (var group in groups)
        {
            SourceGroups.Add(group);
        }

        var selected = _appliedSourceUid.HasValue
            ? SourceGroups.FirstOrDefault(item => item.SourceUid == _appliedSourceUid)
            : SourceGroups[0];
        if (selected is null)
        {
            _appliedSourceUid = null;
            PageIndex = 1;
            selected = SourceGroups[0];
        }

        SelectedSourceGroup = selected;
        PageChangeCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void RestoreAppliedSourceSelection()
    {
        SelectedSourceGroup = SourceGroups.FirstOrDefault(item => item.SourceUid == _appliedSourceUid)
            ?? SourceGroups.FirstOrDefault();
    }

    private static List<string> SplitLines(string value) => value
        .Split(['\r', '\n', ',', ';', '；', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private List<GrossDemandCommandItem> CreateCommandItems(IReadOnlyList<GrossDemandRowModel> selected)
    {
        return selected.Select(item => new GrossDemandCommandItem
        {
            Uid = item.Uid,
            Version = item.Version,
            ExpectedDecompositionRevision = item.DecompositionRevision,
            ClientKey = item.Uid.ToString("N"),
            RowIndex = Items.IndexOf(item),
        }).ToList();
    }

    private void ShowCommandSummary(
        string title,
        IEnumerable<(GrossDemandCommandOutcomeEnum Outcome, string Message)> results)
    {
        var values = results.ToArray();
        var succeeded = values.Count(item => item.Outcome == GrossDemandCommandOutcomeEnum.Succeeded);
        var alreadyApplied = values.Count(item => item.Outcome == GrossDemandCommandOutcomeEnum.AlreadyApplied);
        var failed = values.Length - succeeded - alreadyApplied;
        var summary = $"成功 {succeeded}，已应用 {alreadyApplied}，失败 {failed}。";
        var firstFailure = values.FirstOrDefault(item => item.Outcome == GrossDemandCommandOutcomeEnum.Failed).Message;
        if (!string.IsNullOrWhiteSpace(firstFailure))
        {
            summary += $" 首个失败：{firstFailure}";
        }

        if (failed > 0)
        {
            _messageService.ShowWarning(title, summary);
        }
        else
        {
            _messageService.ShowSuccess(title, summary);
        }
    }

    private async Task<bool> ConfirmDiscardManualChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!ManualEditor.HasUnsavedChanges)
        {
            return true;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "放弃手工毛需求修改",
            "当前手工毛需求有未保存修改，继续将放弃这些修改。",
            "放弃修改",
            cancellationToken: cancellationToken);
        if (confirmed)
        {
            ManualEditor.Close(discardChanges: true);
            ActiveDrawerContent = null;
            IsDrawerOpen = false;
        }

        return confirmed;
    }

    partial void OnIsAllCheckedChanged(bool? value)
    {
        if (_syncingChecks || value is null)
        {
            return;
        }

        _syncingChecks = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsChecked = value.Value;
            }
        }
        finally
        {
            _syncingChecks = false;
            UpdateSelectionCommands();
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GrossDemandRowModel.IsChecked) || _syncingChecks)
        {
            return;
        }

        _syncingChecks = true;
        try
        {
            var checkedCount = CheckedCount;
            IsAllChecked = Items.Count == 0 || checkedCount == 0
                ? false
                : checkedCount == Items.Count ? true : null;
        }
        finally
        {
            _syncingChecks = false;
        }

        UpdateSelectionCommands();
    }

    private void ClearChecks()
    {
        _syncingChecks = true;
        foreach (var item in Items)
        {
            item.IsChecked = false;
        }

        IsAllChecked = false;
        _syncingChecks = false;
        UpdateSelectionCommands();
    }

    private void UpdateSelectionCommands()
    {
        OnPropertyChanged(nameof(CheckedCount));
        DecomposeSelectedCommand.NotifyCanExecuteChanged();
        RollbackSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnManualEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GrossDemandManualEditorViewModel.HasUnsavedChanges) or nameof(GrossDemandManualEditorViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private async void OnManualSaved(object? sender, EventArgs e)
    {
        await LoadPageAsync(refreshSourceGroups: true);
    }

    private bool CanRun() => IsNotBusy;
    private bool CanPageChange() => IsNotBusy && !IsSourceSelected;
    private bool CanSelectSourceGroup(GrossDemandSourceGroupModel? group) => IsNotBusy && group is not null;
    private bool CanUseSelected() => IsNotBusy && SelectedItem is not null;
    private bool CanToggleTrace() => IsTraceOpen || CanUseSelected();
    private bool CanDecomposeSelected() => CanWrite && IsNotBusy && Items.Any(item => item.IsChecked) && Items.Where(item => item.IsChecked).All(item => item.CanDecompose);
    private bool CanRollbackSelected() => CanWrite && IsNotBusy && Items.Any(item => item.IsChecked) && Items.Where(item => item.IsChecked).All(item => item.CanRollback);
    private bool CanEditSelectedManual() => CanWrite && IsNotBusy && SelectedItem is { SourceType: "MD", LifecycleStatus: GrossDemandLifecycleStatusEnum.Active, DecompositionStatus: GrossDemandDecompositionStatusEnum.Pending };
    private bool CanCloseSelectedManual() => CanEditSelectedManual();

    private void DetachRows()
    {
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        DetachRows();
        ManualEditor.PropertyChanged -= OnManualEditorPropertyChanged;
        ManualEditor.Saved -= OnManualSaved;
        ManualEditor.Dispose();
        Trace.Dispose();
    }
}
