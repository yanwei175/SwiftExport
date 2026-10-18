using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;

/// <summary>唯一供应商产品页面，负责供应商选择、分页编辑及显式默认供应商操作。</summary>
public sealed partial class SupplierProductManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private const int DefaultPageSize = 20;
    private readonly ISupplierProductApi _supplierProductApi;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private long _loadVersion;
    private long _lookupVersion;
    private string _appliedKeyword = string.Empty;
    private readonly ICurrentUserService _user;

    /// <summary>注入页面API、缓存和当前用户；不再创建嵌套Workspace编辑器。</summary>
    public SupplierProductManagerViewModel(
        ISupplierProductApi supplierProductApi,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService, IReferenceDataCacheService referenceDataCache, ICurrentUserService currentUser)
    {
        _supplierProductApi = supplierProductApi;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache; _user = currentUser;
        _user.CurrentUserChanged += OnPermissionsChanged;
        Title = "供应商产品管理";
        Products.PropertyChanged += OnListPropertyChanged;
        PropertyChanged += OnPagePropertyChanged;
        InitializeCheckSelection();
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddItemCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(SetProductDefaultSupplierCommand);
        RegisterBusyAwareCommand(RejectCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public SupplierProductListModel Products { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasChanges => Products.HasChanges;
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial SupplierLineModel? ActiveSupplier { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetProductDefaultSupplierCommand))]
    public partial SupplierProductLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial ProductLookupDto? SelectedCandidate { get; set; }
    [ObservableProperty] public partial bool IsLookupBusy { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UncheckSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetProductDefaultSupplierCommand))]
    public partial bool IsCellSelectionMode { get; set; }
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
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
    [ObservableProperty] public partial string StatusText { get; set; } = "请选择已保存供应商后打开供应商产品。";

    /// <summary>独立读取状态允许新供应商替代旧等待，写入保持单一上下文。</summary>
    [ObservableProperty] public partial bool IsLoading { get; set; }

    partial void OnSelectedLineChanged(SupplierProductLineModel? value) => Products.SelectedItem = value;

    /// <summary>显式选择已保存供应商，切换前确认现有产品草稿。</summary>
    public async Task<bool> SelectSupplierAsync(SupplierLineModel? supplier)
    {
        if (!CanChangeSupplier || !(CommitPendingEdits?.Invoke() ?? true)) return false;
        var identity = _user.CurrentUser;
        var target = supplier;
        if (target is null || target.IsNew || target.Uid == Guid.Empty)
        {
            _messageService.ShowWarning("无法管理供应商产品", "请先选择一个已保存供应商。");
            return false;
        }

        if (IsOpen && ActiveSupplier?.Uid == target.Uid)
        {
            return true;
        }

        if (IsOpen && !await ConfirmDiscardAsync())
        {
            return false;
        }

        if (!CanChangeSupplier || !ReferenceEquals(identity, _user.CurrentUser)) return false;
        IsOpen = true;
        await LoadSupplierAsync(target);
        return true;
    }

    /// <summary>导航关闭前确认产品草稿，拒绝时保留当前编辑器。</summary>
    public Task<bool> CloseForNavigationAsync() => CanCloseAsync(CancellationToken.None);



    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SearchAsync(string? query)
    {
        if (query is not null) Keyword = query;
        var requested = Keyword;
        if (!await EnsureCanDiscardChangesAsync()) { Keyword = _appliedKeyword; return; }
        await LoadPageAsync(requestedIndex: 1, requestedKeyword: requested);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardChangesAsync()) { Keyword = _appliedKeyword; return; }
        Keyword = _appliedKeyword;
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddItem()
    {
        if (!CanAdd() || !(CommitPendingEdits?.Invoke() ?? true) || ActiveSupplier is null) return;
        SelectedLine = Products.AddNewItem(
            ActiveSupplier.Uid, ActiveSupplier.SupplierCode, ActiveSupplier.SupplierName,
            ActiveSupplier.Currency, ActiveSupplier.TaxRate);
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDelete() || !(CommitPendingEdits?.Invoke() ?? true)) return;
        var identity = _user.CurrentUser; var lines = DeleteTargets();
        if (lines.Length == 0) return;
        var persistedCount = lines.Count(line => !line.IsNew);
        if (persistedCount > 0 && !await ConfirmActionAsync(
                "删除供应商产品", $"确定删除 {lines.Length} 条供应商产品吗？其中 {persistedCount} 条将在保存时删除。", "删除")) return;
        if (!CanDelete() || !ReferenceEquals(identity, _user.CurrentUser) || !lines.SequenceEqual(DeleteTargets())) return;
        foreach (var line in lines) Products.Remove(line);
        SelectedLine = Products.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !(CommitPendingEdits?.Invoke() ?? true) || !CanSave()) return;
        var identity = _user.CurrentUser;
        await RunWriteAsync(async () =>
        {
            var error = await Products.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                _messageService.ShowWarning("供应商产品未保存", error);
                return;
            }
            if (!CanStageChanges() || !ReferenceEquals(identity, _user.CurrentUser)) return;
            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) return;
            var result = await _apiCallRunner.RunAsync(
                () => _supplierProductApi.BatchSaveAsync(request), "保存供应商产品失败");
            if (!CanStageChanges() || !ReferenceEquals(identity, _user.CurrentUser)) return;
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存供应商产品失败", result.Message);
                return;
            }
            Products.ApplySaveResponse(result.Data);
            TotalCount = Math.Max(0, TotalCount + result.Data.AddSuccessCount - result.Data.DeleteSuccessCount);
            SelectedLine = Products.Items.FirstOrDefault();
            UpdateChangeState();
            _messageService.ShowSuccess("供应商产品已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
        }, "正在保存供应商产品...");
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        if (!CanReject() || !(CommitPendingEdits?.Invoke() ?? true)) return;
        Products.RejectChanges();
        SelectedLine = Products.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSetProductDefaultSupplier))]
    private async Task SetProductDefaultSupplierAsync()
    {
        if (!CanSetProductDefaultSupplier() || !(CommitPendingEdits?.Invoke() ?? true)) return;
        var identity = _user.CurrentUser; var selected = SelectedLine;
        if (selected is null || selected.SkuId is not Guid productId || productId == Guid.Empty
            || !await ConfirmActionAsync(
                "设置产品默认供应商",
                $"将 {selected.SupplierCode} 设置为产品 {selected.ProductSku} 的默认供应商。既有净需求计划不会自动改变，是否继续？",
                "设为默认"))
        {
            return;
        }

        if (!CanSetProductDefaultSupplier() || !ReferenceEquals(identity, _user.CurrentUser) || !ReferenceEquals(selected, SelectedLine)) return;
        await RunWriteAsync(async () =>
        {
            var productResult = await _apiCallRunner.RunAsync(
                () => _productApi.GetByIdAsync(productId),
                "读取产品并发版本失败");
            if (!productResult.Success || productResult.Data is null)
            {
                _messageService.ShowError("无法设置默认供应商", productResult.Message);
                return;
            }

            if (!CanStageDefaultSupplier() || !ReferenceEquals(identity, _user.CurrentUser) || !ReferenceEquals(selected, SelectedLine)) return;
            var result = await _apiCallRunner.RunAsync(
                () => _supplierProductApi.SetProductDefaultSupplierAsync(new SetProductDefaultSupplierCommand
                {
                    SupplierProductId = selected.Uid,
                    ProductId = productId,
                    SupplierId = selected.SupplierId,
                    ExpectedProductVersion = productResult.Data.Version,
                }),
                "设置产品默认供应商失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("设置默认供应商失败", result.Message);
                return;
            }

            if (_disposed || !ReferenceEquals(identity, _user.CurrentUser)) return;
            await LoadPageAsync(allowBusy: true);
            StatusText = $"产品 {selected.ProductSku} 的默认供应商已设置为 {result.Data.SupplierCode}。";
            _messageService.ShowSuccess("默认供应商已更新", StatusText);
        }, "正在设置产品默认供应商...");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        return ChangePageAsync(request.NewIndex, request.NewSize);
    }

    /// <summary>候选读取独立取消，切供应商或释放使旧结果失效。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        if (!CanRun() || !CanWrite || !(CanCreate || CanEdit)) return;
        CancelRead(ref _lookupCts);
        var version = Interlocked.Increment(ref _lookupVersion);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            ClearLookupState(incrementVersion: false);
            return;
        }

        SelectedCandidate = null;
        IsLookupBusy = true;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cts.Token; _lookupCts = cts;
        try
        {
            var result = await _apiCallRunner.RunAsync(() => _productApi.SearchLookupAsync(new ProductLookupRequest
            { Keyword = keyword.Trim(), ExcludeProductId = Guid.Empty, Limit = 20 }, token), token, "搜索产品失败");
            if (_disposed || token.IsCancellationRequested || version != Volatile.Read(ref _lookupVersion)) return;
            ProductCandidates.Clear();
            if (result.Success && result.Data is not null)
                foreach (var item in result.Data) ProductCandidates.Add(item);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!_disposed && ReferenceEquals(_lookupCts, cts)) IsLookupBusy = false;
        }
    }



    /// <summary>候选只填充标准资料和空白描述，不覆盖供应商价格。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void CommitProduct(SupplierProductLineModel? line)
    {
        if (!CanEditRow(line) || SelectedCandidate is null) return;
        var product = SelectedCandidate;
        line!.SkuId = product.Uid;
        line.ProductSku = product.Sku ?? string.Empty;
        line.LinkSku = product.LinkSku ?? string.Empty;
        line.DescriptionEn = product.DescriptionEn ?? string.Empty;
        line.DescriptionZh = product.DescriptionZh ?? string.Empty;
        line.FeaturesZh = product.FeaturesZh ?? string.Empty;
        line.CategoryName = product.CategoryName ?? string.Empty;
        line.MaterialType = product.MaterialType ?? string.Empty;
        line.MainImage = product.MainImage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line.SupplierSkuDescriptionZh))
            line.SupplierSkuDescriptionZh = FirstNonEmpty(product.DescriptionZh, product.DescriptionEn);
        if (string.IsNullOrWhiteSpace(line.SupplierSkuFeaturesZh))
            line.SupplierSkuFeaturesZh = FirstNonEmpty(product.FeaturesZh, product.FeaturesEn);
        SelectedLine = line;
    }

    /// <summary>显式解除可空标准物料关联，保留供应商资料和价格。</summary>
    [RelayCommand(CanExecute = nameof(CanEditRow))]
    private void ClearProduct(SupplierProductLineModel? line)
    {
        if (!CanEditRow(line)) return;
        line!.SkuId = null;
        line.ProductSku = string.Empty;
        line.LinkSku = string.Empty;
        line.DescriptionEn = string.Empty;
        line.DescriptionZh = string.Empty;
        line.FeaturesZh = string.Empty;
        line.CategoryName = string.Empty;
        line.MaterialType = string.Empty;
        line.MainImage = string.Empty;
        SelectedLine = line;
    }

    private async Task ChangePageAsync(int index, int size)
    {
        if (!CanRun() || !await EnsureCanDiscardChangesAsync()) { Keyword = _appliedKeyword; return; }
        Keyword = _appliedKeyword;
        await LoadPageAsync(requestedIndex: Math.Max(1, index), requestedSize: size <= 0 ? DefaultPageSize : size);
    }

    /// <summary>在新供应商查询前清除旧供应商数据显示，再由最新请求接纳产品。</summary>
    private async Task LoadSupplierAsync(SupplierLineModel supplier)
    {
        Interlocked.Increment(ref _loadVersion);
        ClearLookupState();
        Products.Clear(); SelectedLine = null; TotalCount = 0;
        ActiveSupplier = supplier;
        _appliedKeyword = string.Empty;
        Keyword = string.Empty;
        PageIndex = 1;
        await LoadPageAsync();
    }

    /// <summary>查询允许供应商替换在途等待，身份、序号和存活状态共同守护回写。</summary>
    private async Task LoadPageAsync(bool allowBusy = false, int? requestedIndex = null, int? requestedSize = null, string? requestedKeyword = null)
    {
        var supplier = ActiveSupplier;
        if (!CanView || IsBusy && !allowBusy || !IsOpen || supplier is null || supplier.Uid == Guid.Empty) return;
        var identity = _user.CurrentUser;
        var index = requestedIndex ?? PageIndex; var size = requestedSize ?? PageSize;
        var keyword = requestedKeyword ?? _appliedKeyword;
        var version = Interlocked.Increment(ref _loadVersion);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cts.Token; var old = Interlocked.Exchange(ref _pageCts, cts); old?.Cancel(); old?.Dispose();
        IsLoading = true;
        StatusText = $"正在加载供应商 {supplier.SupplierCode} 的产品...";
        try
        {
            // HTTP 与等待取消同时释放旧读取，仍以请求序号和供应商身份拒绝过期结果。
            var result = await _apiCallRunner.RunAsync(() => _supplierProductApi.GetPagedAsync(new SupplierProductSearchRequest
            {
                SupplierId = supplier.Uid,
                Keyword = NullIfWhiteSpace(keyword),
                PageIndex = index,
                PageSize = size,
            }, token).WaitAsync(token), token, "加载供应商产品失败");
            if (!CanView || !ReferenceEquals(identity, _user.CurrentUser) || token.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || ActiveSupplier?.Uid != supplier.Uid) return;
            if (!result.Success)
            {
                StatusText = result.Message;
                Keyword = _appliedKeyword;
                _messageService.ShowError("加载供应商产品失败", result.Message);
                return;
            }

            Products.LoadFromDtos(result.Data?.Items ?? []);
            SelectedLine = Products.Items.FirstOrDefault();
            TotalCount = result.Data?.TotalCount ?? 0;
            PageIndex = result.Data?.PageIndex > 0 ? result.Data.PageIndex : index;
            PageSize = result.Data?.PageSize > 0 ? result.Data.PageSize : size;
            _appliedKeyword = keyword; Keyword = keyword;
            StatusText = $"供应商 {supplier.SupplierCode}，共 {TotalCount} 条供应商产品。";
            UpdateChangeState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!_disposed && ReferenceEquals(_pageCts, cts)) IsLoading = false;
        }
    }



    /// <summary>同一确认拥有草稿放弃权，输入错误或写入 Busy 都不会替换当前编辑上下文。</summary>
    private async Task<bool> EnsureCanDiscardChangesAsync(CancellationToken cancellationToken = default, bool allowNoView = false)
    {
        if (_disposed || IsBusy || IsPrompting || !allowNoView && !CanView || !(CommitPendingEdits?.Invoke() ?? true)) return false;
        if (!Products.HasChanges) return true;
        var identity = _user.CurrentUser;
        if (!await ConfirmActionAsync("放弃供应商产品修改", "当前供应商产品有未保存修改，继续将放弃这些修改。", "继续", cancellationToken)) return false;
        if (_disposed || IsBusy || !allowNoView && (!CanView || !ReferenceEquals(identity, _user.CurrentUser))) return false;
        Products.RejectChanges(); return true;
    }

    /// <summary>删除、默认供应商及放弃草稿共用唯一确认状态，期间冻结其他编辑、写入和关闭。</summary>
    private async Task<bool> ConfirmActionAsync(string title, string message, string button, CancellationToken cancellationToken = default)
    {
        if (_disposed || IsBusy || IsPrompting) return false;
        IsPrompting = true;
        try { return await _dialogService.ConfirmAsync(title, message, button, cancellationToken: cancellationToken) && !_disposed; }
        finally { if (!_disposed) IsPrompting = false; }
    }

    private Task<bool> ConfirmDiscardAsync() => EnsureCanDiscardChangesAsync();

    /// <summary>只清空本页供应商与私有产品状态，并失效在途读取，不修改共享候选缓存。</summary>
    private void CloseCore()
    {
        Interlocked.Increment(ref _loadVersion);
        CancelRead(ref _pageCts); IsLoading = false;
        ClearLookupState();
        Products.Clear();
        SelectedLine = null;
        ActiveSupplier = null;
        _appliedKeyword = string.Empty;
        Keyword = string.Empty;
        TotalCount = 0;
        PageIndex = 1;
        IsOpen = false;
        StatusText = "供应商已清空，请选择已保存供应商。";
        UpdateChangeState();
    }



    private void ClearLookupState(bool incrementVersion = true)
    {
        CancelRead(ref _lookupCts);
        if (incrementVersion) Interlocked.Increment(ref _lookupVersion);
        ProductCandidates.Clear();
        SelectedCandidate = null;
        IsLookupBusy = false;
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateChangeState();

    /// <summary>直接更新唯一页面的草稿与能力，不通过Workspace镜像状态。</summary>
    private void UpdateChangeState()
    {
        IsDirty = Products.HasChanges; OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCapabilities();
    }
    private void NotifyCapabilities()
    {
        RefreshCheckState(); OnPropertyChanged(nameof(CanChangeSupplier)); OnPropertyChanged(nameof(CanEditProducts));
        OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(IsGridReadOnly));
        foreach (var command in new IRelayCommand[] { SearchCommand, RefreshCommand, AddItemCommand, DeleteSelectedCommand, SaveCommand,
                     SetProductDefaultSupplierCommand, RejectCommand, PageChangeCommand, ClearSupplierCommand, FillDownCommand,
                     CommitProductCommand, ClearProductCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>供应商产品页的独立查看许可。</summary>
    public bool CanView => !_disposed && _user.HasPermission(PermissionKeys.SuppliersProductsView);
    /// <summary>新增供应商产品使用创建权限。</summary>
    public bool CanCreate => CanView && _user.HasPermission(PermissionKeys.SuppliersProductsCreate);
    /// <summary>既有供应商产品使用编辑权限。</summary>
    public bool CanEdit => CanView && _user.HasPermission(PermissionKeys.SuppliersProductsEdit);
    /// <summary>既有供应商产品使用删除权限。</summary>
    public bool CanDeleteRecords => CanView && _user.HasPermission(PermissionKeys.SuppliersProductsDelete);
    /// <summary>缺少创建与编辑权限时文本列只读，查看和勾选仍可用。</summary>
    public bool IsGridReadOnly => !CanRun() || !CanWrite || !(CanCreate || CanEdit);
    /// <summary>原生编辑与填充复核行归属、新旧状态和当前供应商身份。</summary>
    public bool CanEditRow(SupplierProductLineModel? row) => CanRun() && CanWrite && row is { IsDeleted: false, IsReadOnly: false }
        && Products.Items.Contains(row) && row.SupplierId == ActiveSupplier!.Uid && (row.IsNew ? CanCreate : CanEdit);
    private bool CanRun() => CanEditProducts && ActiveSupplier is not null;
    private bool CanAdd() => CanRun() && CanWrite && CanCreate;
    private SupplierProductLineModel[] DeleteTargets() => Products.CheckedCount > 0 ? Products.CheckedItems.ToArray() : SelectedLine is null ? [] : [SelectedLine];
    private bool CanDelete() => !IsCellSelectionMode && CanRun() && CanWrite && DeleteTargets() is { Length: > 0 } rows
        && rows.All(row => Products.Items.Contains(row) && !row.IsDeleted && row.SupplierId == ActiveSupplier!.Uid && (row.IsNew ? CanCreate : CanDeleteRecords));
    private bool CanSave() => CanRun() && CanWrite && (CanCreate || CanEdit || CanDeleteRecords) && CanStageChanges();
    private bool CanReject() => CanRun() && Products.HasChanges;
    /// <summary>Busy 内按真实变更类别二次复核，不把本次写入自己的忙碌视为撤权。</summary>
    private bool CanStageChanges() => CanView && CanWrite && ActiveSupplier is not null
        && Products.AllLines.All(row => row.SupplierId == ActiveSupplier.Uid)
        && (Products.AddedItems.Count == 0 || CanCreate) && (Products.ModifiedItems.Count == 0 || CanEdit)
        && (Products.DeletedItems.Count == 0 || CanDeleteRecords);
    private bool CanSetProductDefaultSupplier() => CanRun() && !IsCellSelectionMode && CanStageDefaultSupplier();
    /// <summary>默认供应商修改Product，额外检查已有产品编辑权限和已保存无草稿关联。</summary>
    private bool CanStageDefaultSupplier() => CanView && CanWrite && CanEdit && _user.HasPermission(PermissionKeys.ProductsProductEdit)
        && !Products.HasChanges && ActiveSupplier is not null && SelectedLine is { IsNew: false, IsDeleted: false, IsProductDefaultSupplier: false }
        && Products.Items.Contains(SelectedLine) && SelectedLine.Uid != Guid.Empty && SelectedLine.SkuId is { } id && id != Guid.Empty
        && SelectedLine.SupplierId == ActiveSupplier.Uid;
    /// <summary>撤权使读取失效，保留现有草稿，不取消已发送写入。</summary>
    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _loadVersion); Interlocked.Increment(ref _lookupVersion);
        CancelRead(ref _pageCts); CancelRead(ref _lookupCts); IsLoading = false; IsLookupBusy = false; NotifyCapabilities();
    }

    private static string FirstNonEmpty(string? first, string? second) => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => CanRun() && CanWrite
        && request is not null && request.Fields.All(field => field is nameof(SupplierProductLineModel.SupplierSku)
            or nameof(SupplierProductLineModel.SupplierSkuDescriptionZh)
            or nameof(SupplierProductLineModel.SupplierSkuFeaturesZh)
            or nameof(SupplierProductLineModel.UnitPrice)
            or nameof(SupplierProductLineModel.TaxPrice))
        && request.TryGetRows<SupplierProductLineModel>(Products.Items, out _, out var targets)
        && targets.Count > 0 && targets.All(CanEditRow);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<SupplierProductLineModel>(Products.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(CanEditRow))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(SupplierProductLineModel.SupplierSku): target.SupplierSku = source.SupplierSku; break;
                    case nameof(SupplierProductLineModel.SupplierSkuDescriptionZh): target.SupplierSkuDescriptionZh = source.SupplierSkuDescriptionZh; break;
                    case nameof(SupplierProductLineModel.SupplierSkuFeaturesZh): target.SupplierSkuFeaturesZh = source.SupplierSkuFeaturesZh; break;
                    case nameof(SupplierProductLineModel.UnitPrice): target.UnitPrice = source.UnitPrice; break;
                    case nameof(SupplierProductLineModel.TaxPrice): target.TaxPrice = source.TaxPrice; break;
                }
            }
        }
    }
}
