using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

public sealed partial class NetDemandWorkspaceViewModel : ViewModelBaseEx,
    IAsyncInitializable,
    IDocumentTabViewModel,
    IDocumentTabInitializable,
    IDisposable
{
    public const string RouteKey = "net-demands";

    private readonly INetDemandWorkspaceApi _api;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private long _supplierSearchVersion;
    private bool _initialized;
    private bool _syncingChecks;
    private bool _disposed;
    private NetDemandWorkspaceOpenRequest? _openRequest;

    public NetDemandWorkspaceViewModel(
        INetDemandWorkspaceApi api,
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        IDialogService dialogService,
        IMessageService messageService,
        NetDemandPlanEditorViewModel planEditor,
        NetDemandTraceViewModel trace)
    {
        _api = api;
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        _dialogService = dialogService;
        _messageService = messageService;
        PlanEditor = planEditor;
        Trace = trace;
        Title = "净需求";
        PlanEditor.PropertyChanged += OnPlanEditorPropertyChanged;
        PlanEditor.Saved += OnPlanSaved;

        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(ClearFiltersCommand);
        RegisterBusyAwareCommand(ModifyPlanCommand);
        RegisterBusyAwareCommand(BatchAssignSupplierCommand);
        RegisterBusyAwareCommand(ApplyDefaultSupplierCommand);
        RegisterBusyAwareCommand(OpenTraceCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public NetDemandPlanEditorViewModel PlanEditor { get; }
    public NetDemandTraceViewModel Trace { get; }
    public ObservableCollection<NetDemandRowModel> Items { get; } = [];
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];

    public IReadOnlyList<NetDemandFilterOption<string>> SourceTypeOptions { get; } =
    [
        new("SO", "销售订单"),
        new("MD", "手工需求"),
        new("CR", "投诉补货"),
    ];

    public IReadOnlyList<NetDemandFilterOption<DemandExecutionTypeEnum>> ExecutionTypeOptions { get; } =
    [
        new(DemandExecutionTypeEnum.Purchase, "采购"),
        new(DemandExecutionTypeEnum.Production, "生产"),
        new(DemandExecutionTypeEnum.Outsourcing, "外协"),
    ];

    public IReadOnlyList<NetDemandFilterOption<NetDemandPlanStatusEnum>> PlanStatusOptions { get; } =
    [
        new(NetDemandPlanStatusEnum.UnassignedSupplier, "待分配供应商"),
        new(NetDemandPlanStatusEnum.DeliveryTargetPending, "送达目标待分配"),
        new(NetDemandPlanStatusEnum.ReadyForPurchase, "可转采购"),
        new(NetDemandPlanStatusEnum.PartiallyAllocated, "部分转采购"),
        new(NetDemandPlanStatusEnum.FullyAllocated, "已转采购"),
        new(NetDemandPlanStatusEnum.DownstreamDraft, "采购草稿"),
        new(NetDemandPlanStatusEnum.DownstreamConfirmed, "采购已确认"),
        new(NetDemandPlanStatusEnum.DataCorrupted, "数据异常"),
        new(NetDemandPlanStatusEnum.ExecutionWorkspacePending, "执行工作台待接入"),
    ];

    public IReadOnlyList<NetDemandFilterOption<bool?>> DemandRoleOptions { get; } =
    [
        new(null, "全部角色"),
        new(false, "父件"),
        new(true, "客供件"),
    ];

    public IReadOnlyList<NetDemandFilterOption<bool?>> RemainingOptions { get; } =
    [
        new(null, "全部转单状态"),
        new(true, "仍有可转数量"),
        new(false, "没有可转数量"),
    ];

    public ObservableCollection<NetDemandFilterOption<string>> SelectedSourceTypes { get; } = [];
    public ObservableCollection<NetDemandFilterOption<DemandExecutionTypeEnum>> SelectedExecutionTypes { get; } = [];
    public ObservableCollection<NetDemandFilterOption<NetDemandPlanStatusEnum>> SelectedPlanStatuses { get; } = [];

    public string DocumentTitle => "净需求";
    public bool HasUnsavedChanges => PlanEditor.HasUnsavedChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";
    public int CheckedCount => Items.Count(item => item.IsChecked);

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceNosText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SkusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomerSkusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? DemandRequiredDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? DemandRequiredDateTo { get; set; }

    [ObservableProperty]
    public partial DateTime? PlannedDeliveryDateFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? PlannedDeliveryDateTo { get; set; }

    [ObservableProperty]
    public partial NetDemandFilterOption<bool?>? SelectedDemandRole { get; set; }

    [ObservableProperty]
    public partial NetDemandFilterOption<bool?>? SelectedRemainingOption { get; set; }

    [ObservableProperty]
    public partial SupplierDto? SelectedSupplierFilter { get; set; }

    [ObservableProperty]
    public partial string SupplierKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSupplierLookupBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ModifyPlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyDefaultSupplierCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    public partial NetDemandRowModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial object? ActiveDrawerContent { get; private set; }

    [ObservableProperty]
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
    public partial string ResultSummary { get; set; } = "尚未加载净需求。";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        InitializeDefaults();
        await LoadPageAsync();
    }

    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        _initialized = true;
        InitializeDefaults();
        _openRequest = parameter as NetDemandWorkspaceOpenRequest;
        if (!string.IsNullOrWhiteSpace(_openRequest?.SourceNo))
        {
            SourceNosText = _openRequest.SourceNo;
        }

        await LoadPageAsync(cancellationToken);
    }

    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => ConfirmDiscardPlanChangesAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (!await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        if (query is not null)
        {
            Keyword = query;
        }

        _openRequest = null;
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (await ConfirmDiscardPlanChangesAsync())
        {
            await LoadPageAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearFiltersAsync()
    {
        if (!await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        _openRequest = null;
        Keyword = string.Empty;
        SourceNosText = string.Empty;
        SkusText = string.Empty;
        CustomerSkusText = string.Empty;
        DemandRequiredDateFrom = null;
        DemandRequiredDateTo = null;
        PlannedDeliveryDateFrom = null;
        PlannedDeliveryDateTo = null;
        SelectedDemandRole = DemandRoleOptions[0];
        SelectedRemainingOption = RemainingOptions[0];
        SelectedSupplierFilter = null;
        SupplierKeyword = string.Empty;
        SelectedSourceTypes.Clear();
        SelectedExecutionTypes.Clear();
        SelectedPlanStatuses.Clear();
        PageIndex = 1;
        await LoadPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _supplierSearchVersion);
        IsSupplierLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _supplierApi.GetPagedAsync(new SupplierSearchRequest
            {
                Keyword = keyword?.Trim(),
                PageIndex = 1,
                PageSize = 30,
            }),
            "搜索供应商失败");
        if (version != Volatile.Read(ref _supplierSearchVersion))
        {
            return;
        }

        SupplierCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var supplier in result.Data.Items)
            {
                SupplierCandidates.Add(supplier);
            }
        }

        IsSupplierLookupBusy = false;
    }

    [RelayCommand(CanExecute = nameof(CanModifyPlan))]
    private async Task ModifyPlanAsync()
    {
        if (SelectedItem is not { } selected || !await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        var detail = await LoadDetailAsync(selected.Uid);
        if (detail is null)
        {
            return;
        }

        Trace.Clear();
        PlanEditor.OpenSingle(detail, IsReadOnly);
        ActiveDrawerContent = PlanEditor;
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanBatchAssignSupplier))]
    private async Task BatchAssignSupplierAsync()
    {
        var selected = Items.Where(item => item.IsChecked).ToArray();
        if (selected.Length == 0 || !await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        if (selected.Select(item => item.IsCustomerSupplied).Distinct().Count() > 1)
        {
            _messageService.ShowWarning("不能批量分配供应商", "父件和客供件不能在同一次批量操作中混合提交。");
            return;
        }

        Trace.Clear();
        PlanEditor.OpenBatch(selected, IsReadOnly);
        ActiveDrawerContent = PlanEditor;
        IsDrawerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanApplyDefaultSupplier))]
    private async Task ApplyDefaultSupplierAsync()
    {
        var selected = SelectedItem;
        if (selected is null
            || !await ConfirmDiscardPlanChangesAsync()
            || !await _dialogService.ConfirmAsync(
                "应用产品默认供应商",
                $"将为 {selected.Sku} 读取当前 Product 默认供应商并生成新的计划版本。是否继续？",
                "应用默认"))
        {
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.ApplyDefaultSupplierAsync(new ApplyProductDefaultSupplierCommand
                {
                    CommandId = Guid.NewGuid(),
                    AllocationId = selected.AllocationId,
                    ExpectedVersion = selected.AllocationVersion,
                    Reason = "用户从净需求工作台应用当前产品默认供应商",
                }),
                "应用产品默认供应商失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("应用默认供应商失败", result.Message);
                return;
            }

            ShowCommandSummary("应用默认供应商", result.Data.Items);
            await LoadPageAsync();
        }, "正在应用产品默认供应商...");
    }

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private async Task OpenTraceAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        PlanEditor.Close(discardChanges: true);
        ActiveDrawerContent = Trace;
        IsDrawerOpen = true;
        if (!await Trace.LoadAsync(selected.Uid))
        {
            _messageService.ShowWarning("净需求追溯", Trace.StatusText);
        }
    }

    [RelayCommand]
    private async Task CloseDrawerAsync()
    {
        if (!await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        PlanEditor.Close(discardChanges: true);
        Trace.Clear();
        ActiveDrawerContent = null;
        IsDrawerOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !await ConfirmDiscardPlanChangesAsync())
        {
            return;
        }

        _openRequest = null;
        PageIndex = request.NewIndex;
        PageSize = request.NewSize;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync(CancellationToken externalCancellation = default)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _searchCancellation = cancellation;
        var selectedUid = SelectedItem?.Uid;

        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _apiCallRunner.RunAsync(
                    () => _api.SearchAsync(CreateSearchRequest(), cancellation.Token),
                    cancellation.Token,
                    "查询净需求失败");
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
                    var row = NetDemandRowModel.FromDto(dto);
                    row.PropertyChanged += OnRowPropertyChanged;
                    Items.Add(row);
                }

                TotalCount = result.Data.TotalCount;
                PageIndex = result.Data.PageIndex <= 0 ? PageIndex : result.Data.PageIndex;
                PageSize = result.Data.PageSize <= 0 ? PageSize : result.Data.PageSize;
                SelectedItem = selectedUid is null
                    ? Items.FirstOrDefault()
                    : Items.FirstOrDefault(item => item.Uid == selectedUid) ?? Items.FirstOrDefault();
                ClearChecks();
                ResultSummary = $"共 {TotalCount} 条净需求计划，本页 {Items.Count} 条。";
            }, "正在查询净需求...", skipIfBusy: false);
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

    private async Task<NetDemandDetailDto?> LoadDetailAsync(Guid uid)
    {
        _detailCancellation?.Cancel();
        _detailCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailCancellation = cancellation;
        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.GetDetailAsync(uid, cancellation.Token),
                cancellation.Token,
                "加载净需求详情失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_detailCancellation, cancellation))
            {
                return null;
            }

            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("无法修改净需求计划", result.Message);
                return null;
            }

            return result.Data;
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

    private NetDemandSearchRequest CreateSearchRequest()
    {
        var request = new NetDemandSearchRequest
        {
            Keyword = Keyword.Trim(),
            PageIndex = PageIndex,
            PageSize = PageSize,
            SourceTypes = SelectedSourceTypes.Select(item => item.Value).ToList(),
            SourceNos = SplitLines(SourceNosText),
            Skus = SplitLines(SkusText),
            CustomerSkus = SplitLines(CustomerSkusText),
            DemandRequiredDateFrom = DemandRequiredDateFrom,
            DemandRequiredDateTo = DemandRequiredDateTo,
            PlannedDeliveryDateFrom = PlannedDeliveryDateFrom,
            PlannedDeliveryDateTo = PlannedDeliveryDateTo,
            ExecutionTypes = SelectedExecutionTypes.Select(item => item.Value).ToList(),
            PlanStatuses = SelectedPlanStatuses.Select(item => item.Value).ToList(),
            IsCustomerSupplied = SelectedDemandRole?.Value,
            HasRemainingQty = SelectedRemainingOption?.Value,
        };

        if (SelectedSupplierFilter is not null)
        {
            request.PurchaseSupplierIds.Add(SelectedSupplierFilter.Uid);
        }

        if (_openRequest?.NetDemandId is Guid netDemandId)
        {
            request.NetDemandIds.Add(netDemandId);
        }

        if (_openRequest?.GrossDemandId is Guid grossDemandId)
        {
            request.GrossDemandIds.Add(grossDemandId);
        }

        if (_openRequest?.SourceUid is Guid sourceUid)
        {
            request.SourceUids.Add(sourceUid);
        }

        return request;
    }

    private static List<string> SplitLines(string value) => value
        .Split(['\r', '\n', ',', ';', '；', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private async Task<bool> ConfirmDiscardPlanChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!PlanEditor.HasUnsavedChanges)
        {
            return true;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "放弃净需求计划修改",
            "当前计划编辑有未保存修改，继续将放弃这些修改。",
            "放弃修改",
            cancellationToken: cancellationToken);
        if (confirmed)
        {
            PlanEditor.Close(discardChanges: true);
            ActiveDrawerContent = null;
            IsDrawerOpen = false;
        }

        return confirmed;
    }

    private void InitializeDefaults()
    {
        SelectedDemandRole ??= DemandRoleOptions[0];
        SelectedRemainingOption ??= RemainingOptions[0];
    }

    private void ShowCommandSummary(string title, IReadOnlyList<NetDemandPlanCommandItemResult> items)
    {
        var succeeded = items.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.Succeeded);
        var already = items.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.AlreadyApplied);
        var failed = items.Count - succeeded - already;
        var message = $"成功 {succeeded}，已应用 {already}，失败 {failed}。";
        var firstFailure = items.FirstOrDefault(item => item.Outcome == NetDemandCommandOutcomeEnum.Failed)?.Message;
        if (!string.IsNullOrWhiteSpace(firstFailure))
        {
            message += $" 首个失败：{firstFailure}";
        }

        if (failed > 0)
        {
            _messageService.ShowWarning(title, message);
        }
        else
        {
            _messageService.ShowSuccess(title, message);
        }
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
        if (e.PropertyName != nameof(NetDemandRowModel.IsChecked) || _syncingChecks)
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
        BatchAssignSupplierCommand.NotifyCanExecuteChanged();
    }

    private void OnPlanEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetDemandPlanEditorViewModel.IsDirty) or nameof(NetDemandPlanEditorViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private async void OnPlanSaved(object? sender, EventArgs e)
    {
        PlanEditor.Close(discardChanges: true);
        ActiveDrawerContent = null;
        IsDrawerOpen = false;
        await LoadPageAsync();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanUseSelected() => IsNotBusy && SelectedItem is not null;
    private bool CanModifyPlan() => CanWrite && IsNotBusy && SelectedItem?.CanReplacePlan == true;
    private bool CanApplyDefaultSupplier() => CanWrite && IsNotBusy && SelectedItem?.CanApplyDefaultSupplier == true;
    private bool CanBatchAssignSupplier() => CanWrite
        && IsNotBusy
        && Items.Any(item => item.IsChecked)
        && Items.Where(item => item.IsChecked).All(item => item.CanReplacePlan && item.ExecutionType == DemandExecutionTypeEnum.Purchase);

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
        Interlocked.Increment(ref _supplierSearchVersion);
        DetachRows();
        PlanEditor.PropertyChanged -= OnPlanEditorPropertyChanged;
        PlanEditor.Saved -= OnPlanSaved;
        PlanEditor.Dispose();
        Trace.Dispose();
    }
}
