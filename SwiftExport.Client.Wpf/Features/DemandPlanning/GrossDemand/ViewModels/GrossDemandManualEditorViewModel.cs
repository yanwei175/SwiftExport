using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

public sealed partial class GrossDemandManualEditorViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IGrossDemandWorkspaceApi _api;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private long _searchVersion;
    private bool _disposed;

    public GrossDemandManualEditorViewModel(
        IGrossDemandWorkspaceApi api,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService)
    {
        _api = api;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        Title = "手工毛需求";
        Edit.PropertyChanged += OnEditPropertyChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(SearchProductsCommand);
    }

    public GrossDemandManualEditModel Edit { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasUnsavedChanges => IsOpen && Edit.IsDirty;
    public bool IsNew => Edit.IsNew;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial ProductLookupDto? SelectedProduct { get; set; }

    [ObservableProperty]
    public partial bool IsLookupBusy { get; set; }

    public event EventHandler? Saved;

    public void OpenCreate()
    {
        ClearLookup();
        Edit.ResetForCreate();
        IsOpen = true;
        Title = "新建手工毛需求";
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsNew));
    }

    public void OpenEdit(GrossDemandDetailDto detail)
    {
        ClearLookup();
        Edit.Load(detail);
        IsOpen = true;
        Title = $"编辑手工毛需求 {detail.SourceNo}";
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsNew));
    }

    public void Close(bool discardChanges)
    {
        if (discardChanges)
        {
            Edit.AcceptChanges();
        }

        ClearLookup();
        IsOpen = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _searchVersion);
        ProductCandidates.Clear();
        SelectedProduct = null;
        if (string.IsNullOrWhiteSpace(keyword) || Edit.IsNonStandard)
        {
            IsLookupBusy = false;
            return;
        }

        IsLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _productApi.SearchLookupAsync(new ProductLookupRequest
            {
                Keyword = keyword.Trim(),
                Limit = 20,
            }),
            "搜索产品失败");
        if (version != Volatile.Read(ref _searchVersion))
        {
            return;
        }

        if (result.Success && result.Data is not null)
        {
            foreach (var item in result.Data)
            {
                ProductCandidates.Add(item);
            }
        }

        IsLookupBusy = false;
    }

    [RelayCommand]
    private void CommitProduct()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        Edit.ApplyProduct(SelectedProduct);
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearProduct()
    {
        SelectedProduct = null;
        Edit.ProductId = null;
        Edit.Sku = string.Empty;
        Edit.LinkSku = string.Empty;
        Edit.ImagePath = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!Edit.ValidateForSave(out var validation))
        {
            ValidationSummary = validation;
            _messageService.ShowWarning("手工毛需求未保存", validation);
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = Edit.IsNew
                ? await _apiCallRunner.RunAsync(
                    () => _api.CreateManualAsync(Edit.ToCreateRequest()),
                    "新建手工毛需求失败")
                : await _apiCallRunner.RunAsync(
                    () => _api.UpdateManualAsync(Edit.Uid, Edit.ToUpdateRequest()),
                    "更新手工毛需求失败");
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                _messageService.ShowError("手工毛需求保存失败", result.Message);
                return;
            }

            Edit.Load(result.Data);
            ValidationSummary = null;
            Title = $"编辑手工毛需求 {result.Data.SourceNo}";
            _messageService.ShowSuccess("手工毛需求已保存", result.Data.SourceNo);
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(IsNew));
            Saved?.Invoke(this, EventArgs.Empty);
        }, "正在保存手工毛需求...");
    }

    private bool CanSave() => IsOpen && CanWrite && IsNotBusy && Edit.IsDirty;

    private void OnEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ClearLookup()
    {
        Interlocked.Increment(ref _searchVersion);
        ProductCandidates.Clear();
        SelectedProduct = null;
        IsLookupBusy = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Edit.PropertyChanged -= OnEditPropertyChanged;
        ClearLookup();
    }
}
