using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.ViewModels;

public sealed partial class BomDrawerViewModel : ViewModelBaseEx
{
    private readonly IBomApi _bomApi;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private long _loadVersion;
    private long _searchVersion;
    private ProductLineModel? _pendingProduct;

    public BomDrawerViewModel(
        IBomApi bomApi,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _bomApi = bomApi;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        Boms.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
            RejectCommand.NotifyCanExecuteChanged();
        };
    }

    public BomListModel Boms { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasChanges => Boms.HasChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial ProductLineModel? ActiveProduct { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    public partial BomLineModel? SelectedLine { get; set; }

    [ObservableProperty]
    public partial ProductLookupDto? SelectedCandidate { get; set; }
    [ObservableProperty]
    public partial bool IsLookupBusy { get; set; }
    [ObservableProperty]
    public partial string StatusText { get; set; } = "选择产品后打开 BOM 抽屉。";

    public event EventHandler<ProductLineModel?>? SelectionRestoreRequested;

    public void NotifyProductSelection(ProductLineModel? product)
    {
        _pendingProduct = product;
        if (IsOpen)
        {
            _ = SwitchProductAsync(product);
        }
    }

    [RelayCommand]
    private async Task ToggleAsync(ProductLineModel? product)
    {
        if (IsOpen)
        {
            await CloseAsync();
            return;
        }

        IsOpen = true;
        await SwitchProductAsync(product ?? _pendingProduct, skipDirtyCheck: true);
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        Interlocked.Increment(ref _loadVersion);
        ClearProductLookupState();
        IsOpen = false;
        StatusText = "BOM 抽屉已关闭。";
    }

    [RelayCommand]
    private Task RefreshAsync() => IsOpen ? LoadProductAsync(ActiveProduct) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        if (ActiveProduct is null || string.IsNullOrWhiteSpace(ActiveProduct.BomNo)) return;
        SelectedLine = Boms.AddNewItem(ActiveProduct.BomNo);
    }

    private bool CanAddItem() => IsOpen && ActiveProduct is not null && !string.IsNullOrWhiteSpace(ActiveProduct.BomNo);

    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        if (SelectedLine is null) return;
        Boms.Remove(SelectedLine);
        SelectedLine = Boms.Items.FirstOrDefault();
    }

    private bool CanDeleteItem() => SelectedLine is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var validation = await Boms.ValidateAllAsync();
        if (!string.IsNullOrWhiteSpace(validation))
        {
            _messageService.ShowWarning("BOM 未保存", validation);
            return;
        }

        var request = Boms.CreateSaveRequest();
        if (!request.HasChanges) return;
        var result = await _apiCallRunner.RunAsync(() => _bomApi.BatchSaveAsync(request), "保存 BOM 失败");
        if (!result.Success)
        {
            _messageService.ShowError("保存 BOM 失败", result.Message);
            return;
        }

        Boms.AcceptChanges();
        _messageService.ShowSuccess("BOM 已保存", $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
        await LoadProductAsync(ActiveProduct);
    }

    private bool CanSave() => Boms.HasChanges;

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        Boms.RejectChanges();
        SelectedLine = Boms.Items.FirstOrDefault();
    }

    private bool CanReject() => Boms.HasChanges;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _searchVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ProductCandidates.Clear();
            SelectedCandidate = null;
            IsLookupBusy = false;
            return;
        }

        SelectedCandidate = null;
        IsLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _productApi.SearchLookupAsync(new ProductLookupRequest
            {
                Keyword = keyword.Trim(),
                ExcludeProductId = ActiveProduct?.Uid ?? Guid.Empty,
                Limit = 20,
            }),
            "搜索 BOM 子件失败");
        if (version != Volatile.Read(ref _searchVersion)) return;

        ProductCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var item in result.Data) ProductCandidates.Add(item);
        }

        IsLookupBusy = false;
    }

    [RelayCommand]
    private void CommitProduct(BomLineModel? line)
    {
        if (line is null || SelectedCandidate is null || ActiveProduct is null) return;
        line.BomNo = ActiveProduct.BomNo;
        line.SubSkuId = SelectedCandidate.Uid;
        line.SubSku = SelectedCandidate.Sku ?? string.Empty;
        line.Description = !string.IsNullOrWhiteSpace(SelectedCandidate.DescriptionEn)
            ? SelectedCandidate.DescriptionEn
            : SelectedCandidate.DescriptionZh ?? string.Empty;
        line.Finish = SelectedCandidate.Finish ?? string.Empty;
        line.MainImage = SelectedCandidate.MainImage ?? string.Empty;
        if (line.Numerator <= 0) line.Numerator = 1;
        if (line.Denominator <= 0) line.Denominator = 1;
        SelectedLine = line;
    }

    private async Task SwitchProductAsync(ProductLineModel? product, bool skipDirtyCheck = false)
    {
        if (ReferenceEquals(product, ActiveProduct)) return;
        if (!skipDirtyCheck && !await ConfirmDiscardAsync())
        {
            SelectionRestoreRequested?.Invoke(this, ActiveProduct);
            return;
        }

        await LoadProductAsync(product);
    }

    private async Task LoadProductAsync(ProductLineModel? product)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ClearProductLookupState();
        ActiveProduct = product;
        Boms.LoadFromDtos([]);
        SelectedLine = null;
        if (product is null)
        {
            StatusText = "请选择产品。";
            return;
        }

        if (string.IsNullOrWhiteSpace(product.BomNo))
        {
            StatusText = $"产品 {product.Sku} 尚未建立 BOM。";
            return;
        }

        StatusText = $"正在加载 {product.BomNo}...";
        var result = await _apiCallRunner.RunAsync(() => _bomApi.GetBomByBomNo(product.BomNo), "加载 BOM 失败");
        if (version != Volatile.Read(ref _loadVersion)) return;
        if (!result.Success)
        {
            StatusText = result.Message;
            return;
        }

        Boms.LoadFromDtos(result.Data ?? []);
        SelectedLine = Boms.Items.FirstOrDefault();
        StatusText = $"BOM {product.BomNo}，共 {Boms.Items.Count} 项。";
    }

    private void ClearProductLookupState()
    {
        Interlocked.Increment(ref _searchVersion);
        ProductCandidates.Clear();
        SelectedCandidate = null;
        IsLookupBusy = false;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!Boms.HasChanges) return true;
        var confirmed = await _dialogService.ConfirmAsync("放弃 BOM 修改", "当前 BOM 有未保存修改，继续将放弃这些修改。", "继续");
        if (confirmed) Boms.RejectChanges();
        return confirmed;
    }
}
