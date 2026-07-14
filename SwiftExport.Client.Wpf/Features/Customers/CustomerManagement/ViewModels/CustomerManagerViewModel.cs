using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;

public sealed partial class CustomerManagerViewModel : ViewModelBaseEx, IAsyncInitializable
{
    private const int DefaultPageSize = 20;
    private readonly ICustomerApi _customerApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private bool _initialized;
    private bool _restoringSelection;

    public CustomerManagerViewModel(
        ICustomerApi customerApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        CustomerProductManagerViewModel customerProducts)
    {
        _customerApi = customerApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        CustomerProducts = customerProducts;
        Title = "客户管理";
        Customers.PropertyChanged += OnListPropertyChanged;
        CustomerProducts.SelectionRestoreRequested += (_, customer) => RestoreSelection(customer);
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public CustomerListModel Customers { get; } = new();
    public CustomerProductManagerViewModel CustomerProducts { get; }
    public bool HasChanges => Customers.HasChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial CustomerLineModel? SelectedLine { get; set; }
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
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载客户资料。";

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await LoadPageAsync(false);
    }

    partial void OnSelectedLineChanged(CustomerLineModel? value)
    {
        Customers.SelectedItem = value;
        if (!_restoringSelection) CustomerProducts.NotifyCustomerSelection(value);
    }

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
        SelectedLine = Customers.AddNewCustomer();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        var lines = Customers.CheckedCount > 0
            ? Customers.CheckedItems.ToArray()
            : SelectedLine is null ? [] : [SelectedLine];
        if (lines.Length == 0) return;
        var persistedCount = lines.Count(line => !line.IsNew);
        if (persistedCount > 0 && !await _dialogService.ConfirmAsync(
                "删除客户",
                $"确定删除 {lines.Length} 个客户吗？其中 {persistedCount} 个已保存客户将在保存时删除；有关联客户产品的客户会被后端阻止。",
                "删除")) return;
        foreach (var line in lines) Customers.Remove(line);
        SelectedLine = Customers.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunWriteAsync(async () =>
        {
            var error = await Customers.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("客户资料未保存", error);
                return;
            }
            var request = Customers.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(() => _customerApi.BatchSaveAsync(request), "保存客户资料失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存客户资料失败", result.Message);
                return;
            }
            Customers.ApplySaveResponse(result.Data);
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Customers.Items.FirstOrDefault(line => ReferenceEquals(line, SelectedLine)) ?? Customers.Items.FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("客户资料已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
        }, "正在保存客户资料...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Customers.RejectChanges();
        SelectedLine = Customers.Items.FirstOrDefault();
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
            var result = await _apiCallRunner.RunAsync(() => _customerApi.GetPagedAsync(new CustomerSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize,
            }), "加载客户资料失败");
            if (!result.Success)
            {
                ResultSummary = "客户资料加载失败。";
                _messageService.ShowError("加载客户资料失败", result.Message);
                return;
            }
            Customers.LoadFromDtos(result.Data?.Items ?? []);
            SelectedLine = Customers.Items.FirstOrDefault();
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : PageIndex;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : PageSize;
            ResultSummary = $"共 {TotalCount} 条，本页 {Customers.Items.Count} 条。";
            AcceptChanges();
            UpdateChangeState();
            loaded = true;
        }, "正在加载客户资料...");
        return loaded;
    }

    private async Task<bool> PrepareReloadAsync()
    {
        if (Customers.HasChanges && !await _dialogService.ConfirmAsync(
                "放弃客户资料修改", "当前客户列表有未保存修改，继续将放弃这些修改。", "继续")) return false;
        return await CustomerProducts.CloseForNavigationAsync();
    }

    private void RestoreSelection(CustomerLineModel? customer)
    {
        _restoringSelection = true;
        try { SelectedLine = customer; }
        finally { _restoringSelection = false; }
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    private void UpdateChangeState()
    {
        IsDirty = Customers.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => IsNotBusy;
    private bool CanWriteCommand() => IsNotBusy && CanWrite;
    private bool CanDelete() => IsNotBusy && CanWrite && (Customers.CheckedCount > 0 || SelectedLine is not null);
    private bool CanSave() => IsNotBusy && CanWrite && Customers.HasChanges;
    private bool CanReject() => IsNotBusy && Customers.HasChanges;
}
