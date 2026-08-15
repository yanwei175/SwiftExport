using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;

public sealed partial class SupplierProductWorkspaceViewModel
    : ViewModelBaseEx, IDocumentTabViewModel, IDisposable
{
    public const string RouteKey = "supplier-products";
    private const int LookupPageSize = 20;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private long _supplierSearchVersion;
    private bool _synchronizingSelection;
    private bool _disposed;

    public SupplierProductWorkspaceViewModel(
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        SupplierProductManagerViewModel editor)
    {
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        Editor = editor;
        Title = "供应商产品管理";
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public SupplierProductManagerViewModel Editor { get; }
    public ObservableCollection<SupplierLineModel> SupplierCandidates { get; } = [];
    public string DocumentTitle => Editor.ActiveSupplier is null
        ? "供应商产品管理"
        : $"供应商产品管理 - {Editor.ActiveSupplier.SupplierCode}";
    public bool HasUnsavedChanges => Editor.HasChanges;

    [ObservableProperty]
    public partial string SupplierSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SupplierLineModel? SelectedSupplier { get; set; }

    [ObservableProperty]
    public partial bool IsSupplierLookupBusy { get; set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _supplierSearchVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            SupplierCandidates.Clear();
            IsSupplierLookupBusy = false;
            return;
        }

        IsSupplierLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _supplierApi.GetPagedAsync(new SupplierSearchRequest
            {
                Keyword = keyword.Trim(),
                PageIndex = 1,
                PageSize = LookupPageSize,
            }),
            "搜索供应商失败");

        if (version != Volatile.Read(ref _supplierSearchVersion))
        {
            return;
        }

        SupplierCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var dto in result.Data.Items)
            {
                var candidate = new SupplierLineModel();
                candidate.Load(dto);
                SupplierCandidates.Add(candidate);
            }
        }

        IsSupplierLookupBusy = false;
    }

    [RelayCommand]
    private async Task CommitSupplierAsync()
    {
        var candidate = SelectedSupplier;
        if (candidate is null)
        {
            return;
        }

        if (await Editor.SelectSupplierAsync(candidate))
        {
            SynchronizeSelection(candidate);
            return;
        }

        SynchronizeSelection(Editor.ActiveSupplier);
    }

    [RelayCommand]
    private async Task ClearSupplierAsync()
    {
        if (!await Editor.CloseForNavigationAsync())
        {
            SynchronizeSelection(Editor.ActiveSupplier);
            return;
        }

        SynchronizeSelection(null);
        ClearSupplierCandidates();
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
        Interlocked.Increment(ref _supplierSearchVersion);
        Editor.PropertyChanged -= OnEditorPropertyChanged;
        SupplierCandidates.Clear();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(SupplierProductManagerViewModel.HasChanges)
                or nameof(SupplierProductManagerViewModel.ActiveSupplier)
                or nameof(SupplierProductManagerViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        if (!_synchronizingSelection
            && e.PropertyName == nameof(SupplierProductManagerViewModel.IsOpen)
            && !Editor.IsOpen)
        {
            SynchronizeSelection(null);
            ClearSupplierCandidates();
        }
    }

    private void SynchronizeSelection(SupplierLineModel? supplier)
    {
        _synchronizingSelection = true;
        try
        {
            SelectedSupplier = supplier;
            SupplierSearchText = supplier?.SupplierCode ?? string.Empty;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void ClearSupplierCandidates()
    {
        Interlocked.Increment(ref _supplierSearchVersion);
        SupplierCandidates.Clear();
        IsSupplierLookupBusy = false;
    }
}
