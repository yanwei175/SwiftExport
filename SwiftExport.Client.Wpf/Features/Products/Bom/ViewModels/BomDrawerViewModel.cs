using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.ViewModels;

/// <summary>物料管理页的 BOM 抽屉，只允许成品维护当前组成，不按供货方式限制父件。</summary>
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
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
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

    /// <summary>同步物料选择并刷新入口资格，抽屉已打开时沿用原有草稿确认流程。</summary>
    public void NotifyProductSelection(ProductLineModel? product)
    {
        _pendingProduct = product;
        ToggleCommand.NotifyCanExecuteChanged();
        if (IsOpen)
        {
            _ = SwitchProductAsync(product);
        }
    }

    /// <summary>由物料列表既有行变化事件刷新入口；失去成品资格时使在途组成失效，保留本地草稿。</summary>
    public void NotifyParentEligibilityChanged()
    {
        ToggleCommand.NotifyCanExecuteChanged();
        AddItemCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteItemCommand.NotifyCanExecuteChanged();
        if (ActiveProduct is not null && (ActiveProduct.MaterialType != MaterialTypesEnum.成品 || !ActiveProduct.RequiresBom))
        {
            Interlocked.Increment(ref _loadVersion);
            StatusText = "只有勾选需要 BOM 的成品可以维护组成。";
        }
    }

    /// <summary>成品可打开组成抽屉；已打开的抽屉始终允许按原流程申请关闭。</summary>
    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync(ProductLineModel? product)
    {
        if (!CanToggle(product)) return;
        if (IsOpen)
        {
            await CloseAsync();
            return;
        }

        IsOpen = true;
        await SwitchProductAsync(product ?? _pendingProduct, skipDirtyCheck: true);
    }

    private bool CanToggle(ProductLineModel? product) => IsOpen || (product ?? _pendingProduct) is { MaterialType: MaterialTypesEnum.成品, RequiresBom: true };

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

    /// <summary>只为当前有编号的成品新增组成；直接执行命令也必须重新核对父件资格。</summary>
    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        var product = ActiveProduct;
        if (!CanAddItem() || product is null) return;
        SelectedLine = Boms.AddNewItem(product.BomNo);
    }

    /// <summary>父件资格取当前行的物料类型，不缓存打开时状态，也不排除采购或外协成品。</summary>
    private bool CanAddItem() => IsOpen && ActiveProduct is { MaterialType: MaterialTypesEnum.成品, RequiresBom: true } && !string.IsNullOrWhiteSpace(ActiveProduct.BomNo);

    /// <summary>父件失去成品资格后保留已有草稿，不再允许删除组成。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        if (!CanDeleteItem()) return;
        Boms.Remove(SelectedLine);
        SelectedLine = Boms.Items.FirstOrDefault();
    }

    private bool CanDeleteItem() => CanAddItem() && SelectedLine is not null;

    /// <summary>提交前重新核对成品资格，类型在打开后改变时不写入并保留草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        var validation = await Boms.ValidateAllAsync();
        if (!string.IsNullOrWhiteSpace(validation))
        {
            _messageService.ShowWarning("BOM 未保存", validation);
            return;
        }

        if (!CanSave()) return;
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

    private bool CanSave() => CanAddItem() && Boms.HasChanges;

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

    /// <summary>父件仍是成品时接纳所选子件，不额外限制子件物料类型。</summary>
    [RelayCommand]
    private void CommitProduct(BomLineModel? line)
    {
        var product = ActiveProduct;
        if (!CanAddItem() || line is null || SelectedCandidate is null || product is null) return;
        line.BomNo = product.BomNo;
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

    /// <summary>非成品的遗留 BOM 编号不能作为读取组成入口，选择切换仍使旧读取结果失效。</summary>
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

        if (product.MaterialType != MaterialTypesEnum.成品 || !product.RequiresBom)
        {
            StatusText = "只有勾选需要 BOM 的成品可以维护组成。";
            return;
        }

        if (string.IsNullOrWhiteSpace(product.BomNo))
        {
            StatusText = $"产品 {product.Sku} 尚未建立 BOM。";
            return;
        }

        var bomNo = product.BomNo;
        StatusText = $"正在加载 {bomNo}...";
        var result = await _apiCallRunner.RunAsync(() => _bomApi.GetBomByBomNo(bomNo), "加载 BOM 失败");
        if (version != Volatile.Read(ref _loadVersion)) return;
        // 等待期间可在物料表修改类型或编号，旧响应不能重新接纳到变化后的父件。
        if (product.MaterialType != MaterialTypesEnum.成品 || !product.RequiresBom || product.BomNo != bomNo)
        {
            StatusText = "物料类型或 BOM 编号已变化，请重新选择或刷新。";
            return;
        }
        if (!result.Success)
        {
            StatusText = result.Message;
            return;
        }

        Boms.LoadFromDtos(result.Data ?? []);
        SelectedLine = Boms.Items.FirstOrDefault();
        StatusText = $"BOM {bomNo}，共 {Boms.Items.Count} 项。";
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
