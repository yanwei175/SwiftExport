using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;

public sealed partial class SupplierManagerViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private bool _initialized;
    private bool _restoringSelection;

    public SupplierManagerViewModel(
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        SupplierProductManagerViewModel supplierProducts)
    {
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        SupplierProducts = supplierProducts;
        Title = "供应商管理";
        Suppliers.PropertyChanged += OnListPropertyChanged;
        SupplierProducts.SelectionRestoreRequested += (_, supplier) => RestoreSelection(supplier);
        SupplierProducts.PropertyChanged += OnSupplierProductsPropertyChanged;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(ToggleSupplierProductsCommand);
    }

    public SupplierListModel Suppliers { get; } = new();
    public SupplierProductManagerViewModel SupplierProducts { get; }
    public bool HasChanges => Suppliers.HasChanges;
    public bool CanChangeSelectionMode => !SupplierProducts.IsOpen;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial SupplierLineModel? SelectedLine { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSupplierProductsCommand))]
    public partial bool IsCellSelectionMode { get; set; }
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
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载供应商资料。";

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadPageAsync(false);
    }

    partial void OnSelectedLineChanged(SupplierLineModel? value)
    {
        Suppliers.SelectedItem = value;
        if (!_restoringSelection) SupplierProducts.NotifySupplierSelection(value);
    }

    partial void OnIsCellSelectionModeChanged(bool value)
    {
        if (value && SupplierProducts.IsOpen)
        {
            IsCellSelectionMode = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleSupplierProducts))]
    private Task ToggleSupplierProductsAsync() =>
        SupplierProducts.ToggleForSupplierAsync(SelectedLine);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null) Keyword = query;
        if (!await PrepareReloadAsync()) return;
        PageIndex = 1;
        await LoadPageAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => LoadPageAsync(true);

    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        SelectedLine = Suppliers.AddNewSupplier();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var lines = Suppliers.CheckedCount > 0
            ? Suppliers.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (lines.Length == 0) return;
        var persistedCount = lines.Count(line => !line.IsNew);
        if (persistedCount > 0 && !await _dialogService.ConfirmAsync(
                "删除供应商",
                $"确定删除 {lines.Length} 个供应商吗？其中 {persistedCount} 个已保存供应商将在保存时删除；有关联供应商产品的供应商会被后端阻止。",
                "删除")) return;
        foreach (var line in lines) Suppliers.Remove(line);
        SelectedLine = Suppliers.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var error = await Suppliers.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("供应商资料未保存", error);
                return;
            }
            var request = Suppliers.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(() => _supplierApi.BatchSaveAsync(request), "保存供应商资料失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存供应商资料失败", result.Message);
                return;
            }
            Suppliers.ApplySaveResponse(result.Data);
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Suppliers.Items.FirstOrDefault(line => ReferenceEquals(line, SelectedLine)) ?? Suppliers.Items.FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("供应商资料已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
        }, "正在保存供应商资料...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Suppliers.RejectChanges();
        SelectedLine = Suppliers.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        return ChangePageAsync(request.NewIndex, request.NewSize);
    }

    private async Task ChangePageAsync(int index, int size)
    {
        if (!await PrepareReloadAsync()) return;
        PageIndex = Math.Max(1, index);
        PageSize = size <= 0 ? DefaultPageSize : size;
        await LoadPageAsync(false);
    }

    private async Task<bool> LoadPageAsync(bool confirmChanges)
    {
        if (confirmChanges && !await PrepareReloadAsync()) return false;
        var loaded = false;
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(() => _supplierApi.GetPagedAsync(new SupplierSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize,
            }), "加载供应商资料失败");
            if (!result.Success)
            {
                ResultSummary = "供应商资料加载失败。";
                _messageService.ShowError("加载供应商资料失败", result.Message);
                return;
            }
            Suppliers.LoadFromDtos(result.Data?.Items ?? []);
            SelectedLine = Suppliers.Items.FirstOrDefault();
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 条，本页 {Suppliers.Items.Count} 条。";
            AcceptChanges();
            UpdateChangeState();
            loaded = true;
        }, "正在加载供应商资料...");
        return loaded;
    }

    private async Task<bool> PrepareReloadAsync()
    {
        if (Suppliers.HasChanges && !await _dialogService.ConfirmAsync(
                "放弃供应商资料修改", "当前供应商列表有未保存修改，继续将放弃这些修改。", "继续")) return false;
        return await SupplierProducts.CloseForNavigationAsync();
    }

    private void RestoreSelection(SupplierLineModel? supplier)
    {
        _restoringSelection = true;
        try { SelectedLine = supplier; }
        finally { _restoringSelection = false; }
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    private void OnSupplierProductsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(SupplierProductManagerViewModel.IsOpen))
        {
            if (SupplierProducts.IsOpen && IsCellSelectionMode)
            {
                IsCellSelectionMode = false;
            }

            OnPropertyChanged(nameof(CanChangeSelectionMode));
            ToggleSupplierProductsCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateChangeState()
    {
        IsDirty = Suppliers.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanToggleSupplierProducts() => SupplierProducts.IsOpen || !IsCellSelectionMode;
    private bool CanWriteCommand() => IsNotBusy && CanWrite;
    private bool CanDelete() => IsNotBusy && CanWrite && (Suppliers.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsNotBusy && CanWrite && Suppliers.HasChanges;
    private bool CanReject() => IsNotBusy && Suppliers.HasChanges;
}
