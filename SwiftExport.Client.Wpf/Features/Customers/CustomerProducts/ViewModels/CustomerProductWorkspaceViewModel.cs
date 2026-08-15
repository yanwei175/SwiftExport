using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

public sealed partial class CustomerProductWorkspaceViewModel
    : ViewModelBaseEx, IDocumentTabViewModel, IDisposable
{
    public const string RouteKey = "customer-products";
    private const int LookupPageSize = 20;
    private readonly ICustomerApi _customerApi;
    private readonly IApiCallRunner _apiCallRunner;
    private long _customerSearchVersion;
    private bool _synchronizingSelection;
    private bool _disposed;

    public CustomerProductWorkspaceViewModel(
        ICustomerApi customerApi,
        IApiCallRunner apiCallRunner,
        CustomerProductManagerViewModel editor)
    {
        _customerApi = customerApi;
        _apiCallRunner = apiCallRunner;
        Editor = editor;
        Title = "客户产品管理";
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public CustomerProductManagerViewModel Editor { get; }
    public ObservableCollection<CustomerLineModel> CustomerCandidates { get; } = [];
    public string DocumentTitle => Editor.ActiveCustomer is null
        ? "客户产品管理"
        : $"客户产品管理 - {Editor.ActiveCustomer.CustomerCode}";
    public bool HasUnsavedChanges => Editor.HasChanges;

    [ObservableProperty]
    public partial string CustomerSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CustomerLineModel? SelectedCustomer { get; set; }

    [ObservableProperty]
    public partial bool IsCustomerLookupBusy { get; set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchCustomersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _customerSearchVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            CustomerCandidates.Clear();
            IsCustomerLookupBusy = false;
            return;
        }

        IsCustomerLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _customerApi.GetPagedAsync(new CustomerSearchRequest
            {
                Keyword = keyword.Trim(),
                PageIndex = 1,
                PageSize = LookupPageSize,
            }),
            "搜索客户失败");

        if (version != Volatile.Read(ref _customerSearchVersion))
        {
            return;
        }

        CustomerCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var dto in result.Data.Items)
            {
                var candidate = new CustomerLineModel();
                candidate.Load(dto);
                CustomerCandidates.Add(candidate);
            }
        }

        IsCustomerLookupBusy = false;
    }

    [RelayCommand]
    private async Task CommitCustomerAsync()
    {
        var candidate = SelectedCustomer;
        if (candidate is null)
        {
            return;
        }

        if (await Editor.SelectCustomerAsync(candidate))
        {
            SynchronizeSelection(candidate);
            return;
        }

        SynchronizeSelection(Editor.ActiveCustomer);
    }

    [RelayCommand]
    private async Task ClearCustomerAsync()
    {
        if (!await Editor.CloseForNavigationAsync())
        {
            SynchronizeSelection(Editor.ActiveCustomer);
            return;
        }

        SynchronizeSelection(null);
        ClearCustomerCandidates();
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Editor.CloseForNavigationAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _customerSearchVersion);
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        CustomerCandidates.Clear();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(CustomerProductManagerViewModel.HasChanges)
                or nameof(CustomerProductManagerViewModel.ActiveCustomer)
                or nameof(CustomerProductManagerViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        if (!_synchronizingSelection
            && e.PropertyName == nameof(CustomerProductManagerViewModel.IsOpen)
            && !Editor.IsOpen)
        {
            SynchronizeSelection(null);
            ClearCustomerCandidates();
        }
    }

    private void SynchronizeSelection(CustomerLineModel? customer)
    {
        _synchronizingSelection = true;
        try
        {
            SelectedCustomer = customer;
            CustomerSearchText = customer?.CustomerCode ?? string.Empty;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void ClearCustomerCandidates()
    {
        Interlocked.Increment(ref _customerSearchVersion);
        CustomerCandidates.Clear();
        IsCustomerLookupBusy = false;
    }
}
