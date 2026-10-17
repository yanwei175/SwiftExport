using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.ViewModels;

/// <summary>独立 BOM 管理页：成品分页、当前共享组成编辑及绑定/复制/只读结构操作。</summary>
public sealed partial class BomManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private readonly IBomApi _api;
    private readonly IProductApi _productsApi;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IBomManagementDialogService _bomDialogs;
    private readonly ICurrentUserService _user;
    private readonly ILogger<BomManagerViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pageCts, _detailCts, _lookupCts;
    private BomProductSearchRequest? _applied;
    private Guid? _loadedProductId;
    private bool _initialized, _disposed, _unknownSave;

    /// <summary>注入查询、会话和 UI 抽象，复用基类 Busy 与组成集合，不持有 View。</summary>
    public BomManagerViewModel(IBomApi api, IProductApi productsApi, IApiCallRunner runner,
        IReferenceDataCacheService cache, ICurrentUserService user, IMessageService messages,
        IDialogService dialogs, IBomManagementDialogService bomDialogs, ILogger<BomManagerViewModel> logger)
    {
        _api = api; _productsApi = productsApi; _runner = runner; ReferenceDataCache = cache;
        _user = user; _messages = messages; _dialogs = dialogs; _bomDialogs = bomDialogs; _logger = logger;
        Title = "BOM 管理";
        Boms.ListStateChanged += OnLinesChanged;
        PropertyChanged += OnPageStateChanged;
        _user.CurrentUserChanged += OnPermissionsChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(AddItemCommand);
        RegisterBusyAwareCommand(DeleteItemCommand);
    }

    /// <summary>保持现有 Shell 路由身份，重复打开只激活当前实例。</summary>
    public const string RouteKey = "product-bom";
    /// <summary>主表只读成品行。</summary>
    public ObservableCollection<BomProductItemModel> Products { get; } = [];
    /// <summary>当前 BOM 的可编辑组成与删除池。</summary>
    public BomListModel Boms { get; } = new();
    /// <summary>复用会话基础资料，不在页面创建第二份缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache { get; }
    /// <summary>工具栏分类多选快照。</summary>
    public ObservableCollection<ProductCategoryCacheNode> FilterCategories { get; } = [];
    /// <summary>工具栏颜色多选快照。</summary>
    public ObservableCollection<ProductColorDto> FilterColors { get; } = [];
    /// <summary>当前子件编辑器的远程候选。</summary>
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    /// <summary>绑定筛选显示值；契约使用可空布尔值而非中文判断。</summary>
    public IReadOnlyList<string> BindingFilters { get; } = ["全部", "已绑定", "待建"];
    /// <summary>桥接视图中的原生编辑提交，转换失败时不继续保存或导航。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>集合替换期间阻止视觉选择事件发起重复查询。</summary>
    public bool IsApplyingSelection { get; private set; }
    /// <inheritdoc />
    public string DocumentTitle => Title;
    /// <inheritdoc />
    public bool HasUnsavedChanges => Boms.HasChanges;
    /// <summary>主表可快速切换以替代旧详情请求；提交或确认期间不能切换。</summary>
    public bool CanSelectProduct => !_disposed && IsNotBusy && !IsPrompting && !IsQuerying;
    /// <summary>只有加载成功且身份匹配的组成允许操作。</summary>
    public bool IsDetailReady => CanSelectProduct && !IsLoadingBom && SelectedProduct is { IsBound: true } product && _loadedProductId == product.Uid;
    /// <summary>供原生编辑事件判断的当前编辑能力。</summary>
    public bool CanEditComponents => IsDetailReady && (_user.HasPermission(PermissionKeys.ProductsBomCreate) || _user.HasPermission(PermissionKeys.ProductsBomEdit));
    /// <summary>子件身份更换还需要产品查询权限，不影响已有行的比例和备注编辑。</summary>
    public bool CanLookupProducts => CanEditComponents && !_unknownSave && _user.HasPermission(PermissionKeys.ProductsProductView);
    /// <summary>结果不明必须先刷新核实，不能盲目重复提交新增。</summary>
    public bool IsSaveOutcomeUnknown => _unknownSave;
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial int BindingFilterIndex { get; set; }
    [ObservableProperty] public partial BomProductItemModel? SelectedProduct { get; set; }
    [ObservableProperty] public partial BomLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial ProductLookupDto? SelectedCandidate { get; set; }
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial bool IsQuerying { get; set; }
    [ObservableProperty] public partial bool IsLoadingBom { get; set; }
    [ObservableProperty] public partial bool IsLookupBusy { get; set; }
    [ObservableProperty] public partial bool IsPrompting { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载成品。";
    [ObservableProperty] public partial string BomStatusText { get; set; } = "请选择成品。";

    /// <summary>同一页签初始化一次，缓存失败不阻断有权限的 BOM 查询。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        await ReferenceDataCache.EnsureLoadedAsync();
        if (!_disposed) await LoadPageCoreAsync(CreateRequest(1));
    }

    /// <summary>查询会替换编辑上下文，先处理未保存变更，再应用新筛选。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (!await ConfirmDiscardAsync()) { RestoreFilters(); return; }
        await LoadPageCoreAsync(CreateRequest(1));
    }
    /// <summary>显式刷新当前页，成功读取后才替换组成和脏状态。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!await ConfirmDiscardAsync()) { RestoreFilters(); return; }
        await LoadPageCoreAsync(CreateRequest(PageIndex));
    }
    /// <summary>接纳分页控件的页码与页大小请求，取消时保持已应用分页及筛选。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return;
        if (!await ConfirmDiscardAsync()) { RestoreFilters(); return; }
        PageSize = request.NewSize;
        await LoadPageCoreAsync(CreateRequest(Math.Max(1, request.NewIndex)));
    }
    /// <summary>视图选择事件使用明确命令，setter 不直接调用 API；取消或失败恢复原主记录。</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanSelectProduct))]
    private async Task SelectProductAsync(BomProductItemModel? product)
    {
        if (!CanSelectProduct || product is null || !await ConfirmDiscardAsync()) { RestoreSelection(); return; }
        await LoadBomCoreAsync(product);
    }

    /// <summary>为已成功加载的共享编号新增组成，子件必须通过 Lookup 选定。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddItem()
    {
        if (!CanAdd() || !CommitEdits()) return;
        SelectedLine = Boms.AddNewItem(SelectedProduct!.BomNo);
    }
    /// <summary>删除只改变本地变更集；持久化删除需权限并进入基类删除池。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteItem()
    {
        if (!CanDelete() || !CommitEdits()) return;
        Boms.Remove(SelectedLine!);
        SelectedLine = Boms.Items.FirstOrDefault();
    }
    /// <summary>保存前提交控件编辑、校验本地结构与共享影响，失败保留当前变更。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !CommitEdits()) return;
        var error = await Boms.ValidateAllAsync();
        error ??= Boms.Items.Count == 0 ? "至少需要保留一条组成。" : null;
        error ??= Boms.Items.GroupBy(line => line.SubSkuId).Any(group => group.Count() > 1) ? "BOM 子件不能重复。" : null;
        error ??= Boms.Items.Any(line => line.SubSkuId == SelectedProduct!.Uid) ? "成品不能引用自己作为子件。" : null;
        if (!string.IsNullOrWhiteSpace(error)) { ValidationSummary = error; _messages.ShowWarning("BOM 未保存", error); return; }
        IsPrompting = true;
        try
        {
            // 提交前重新读取共享人数，不能依赖打开页面时的过期计数。
            var info = await _runner.RunAsync(() => _api.GetNumberInfoAsync(SelectedProduct!.BomNo, _lifetime.Token), _lifetime.Token, "查询共享影响失败");
            if (_disposed || !info.Success || info.Data is null) { if (!_disposed) ValidationSummary = info.Message; return; }
            SelectedProduct!.SharedProductCount = info.Data.BoundProductCount;
            if (info.Data.BoundProductCount > 1 && !await _dialogs.ConfirmAsync("保存共享 BOM",
                $"BOM {SelectedProduct.BomNo} 由 {info.Data.BoundProductCount} 个产品共享，保存将影响这些产品的当前组成。", "保存", cancellationToken: _lifetime.Token)) return;
        }
        finally { IsPrompting = false; }
        if (!CanSave()) return;
        var product = SelectedProduct!;
        var request = Boms.CreateSaveRequest();
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存 BOM 失败");
            if (_disposed) return;
            if (!result.Success || result.Data is null || !result.Data.AllSucceeded)
            {
                _unknownSave = result.Error?.Code == ServiceErrorCodes.OutcomeUnknown;
                ValidationSummary = result.Message;
                _messages.ShowError("BOM 未保存", result.Message);
                NotifyCapabilities();
                return;
            }
            if (!Boms.ApplySaveResponse(result.Data))
            {
                _unknownSave = true; ValidationSummary = "保存回执不完整，结果无法确认，请刷新核实后继续。";
                _messages.ShowWarning("BOM 保存待核实", ValidationSummary); NotifyCapabilities(); return;
            }
            ValidationSummary = null;
            _messages.ShowSuccess("BOM 已保存", $"已提交 {result.Data.SuccessCount} 条变更。");
            if (!await LoadBomCoreAsync(product)) _messages.ShowWarning("保存成功", "组成已保存，但刷新失败，请手动刷新。");
        }, "正在保存 BOM...");
    }

    /// <summary>恢复本地组成，结果未知时仍要求先刷新核实。</summary>
    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        if (!CanReject() || !CommitEdits()) return;
        Boms.RejectChanges(); SelectedLine = Boms.Items.FirstOrDefault(); ValidationSummary = null;
    }
    /// <summary>绑定由独立弹窗完成，成功后原位回填字段；待建筛选下继续保留目标以便录入组成。</summary>
    [RelayCommand(CanExecute = nameof(CanBind))]
    private async Task BindAsync()
    {
        if (!CanBind() || !CommitEdits()) return;
        var product = SelectedProduct!;
        IsPrompting = true;
        try
        {
            var result = await _bomDialogs.BindAsync(product, _lifetime.Token);
            if (_disposed) return;
            if (result.OutcomeUnknown) { _unknownSave = true; ValidationSummary = "绑定结果未知，请刷新成品核实后继续。"; NotifyCapabilities(); return; }
            if (result.Reason != DialogResult.Primary || result.Saved is null) return;
            product.BomNo = result.BomNo; product.BomVersion = Math.Max(1, product.BomVersion); product.IsBound = true;
            product.Version = result.Saved.NewVersion ?? product.Version;
            _messages.ShowSuccess("绑定已保存", "请继续维护组成并保存；刷新时会重新应用主表筛选。");
            if (!await LoadBomCoreAsync(product)) _messages.ShowWarning("绑定已保存", "绑定已保存，但组成刷新失败，请手动刷新。");
        }
        finally { IsPrompting = false; }
    }
    /// <summary>复制只用于无编辑的空 BOM，来源行转换为本地新增，用户仍需明确保存。</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyAsync()
    {
        if (!CanCopy() || !CommitEdits()) return;
        var product = SelectedProduct!;
        IsPrompting = true;
        try
        {
            var selection = await _bomDialogs.SelectSourceAsync(product.Uid, _lifetime.Token);
            if (_disposed || selection.Reason != DialogResult.Primary || selection.Product is null) return;
            var targetInfo = await _runner.RunAsync(() => _api.GetNumberInfoAsync(product.BomNo, _lifetime.Token), _lifetime.Token, "核实空 BOM 失败");
            if (_disposed) return;
            if (!targetInfo.Success || targetInfo.Data is null) { _messages.ShowWarning("不能复制", targetInfo.Message); return; }
            if (targetInfo.Data.StoredComponentCount > 0) { _messages.ShowWarning("不能复制", "目标组成已变化或存在异常行，请刷新核实。"); return; }
            var source = await _runner.RunAsync(() => _api.GetBomByBomNo(selection.Product.BomNo, _lifetime.Token), _lifetime.Token, "读取复制来源失败");
            if (_disposed) return;
            if (!source.Success || source.Data is not { Count: > 0 })
            { _messages.ShowWarning("不能复制", source.Success ? "来源已无有效组成，请重新选择或刷新。" : source.Message); return; }
            Boms.CopyIntoEmpty(product.BomNo, source.Data);
            SelectedLine = Boms.Items.FirstOrDefault();
            BomStatusText = $"已复制 {Boms.Items.Count} 项到 {product.BomNo}，请保存。";
        }
        finally { IsPrompting = false; }
    }
    /// <summary>校验只针对已保存结构，不用服务端校验替代本地未保存内容检查。</summary>
    [RelayCommand(CanExecute = nameof(CanReadStructure))]
    private async Task ValidateAsync()
    {
        if (!CanReadStructure() || !CommitEdits()) return;
        if (HasUnsavedChanges) { _messages.ShowInfo("请先保存", "校验针对已保存结构，请先保存当前修改。"); return; }
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ValidateBomAsync(SelectedProduct!.Sku, _lifetime.Token), _lifetime.Token, "校验 BOM 失败");
            if (_disposed) return;
            BomStatusText = result.Success && result.Data ? "已保存 BOM 校验通过。" : result.Message;
            if (result.Success && result.Data) _messages.ShowSuccess("BOM 校验", BomStatusText);
            else _messages.ShowWarning("BOM 校验", BomStatusText);
        }, "正在校验 BOM...");
    }
    /// <summary>结构树读取和展示均为只读，关闭后返回原选中成品。</summary>
    [RelayCommand(CanExecute = nameof(CanReadStructure))]
    private async Task ShowTreeAsync()
    {
        if (!CanReadStructure() || !CommitEdits()) return;
        if (HasUnsavedChanges) { _messages.ShowInfo("请先保存", "结构树展示已保存内容，请先保存当前修改。"); return; }
        await RunBusyAsync(async () =>
        {
            var sku = SelectedProduct!.Sku;
            var result = await _runner.RunAsync(() => _api.GetBomTreeAsync(sku, _lifetime.Token), _lifetime.Token, "读取 BOM 结构失败");
            if (_disposed) return;
            if (!result.Success) { _messages.ShowError("读取 BOM 结构失败", result.Message); return; }
            await _bomDialogs.ShowTreeAsync(sku, result.Data ?? [], _lifetime.Token);
        }, "正在读取 BOM 结构...");
    }

    /// <summary>远程 Lookup 独立取消并防止旧候选覆盖新候选；不在 setter 中执行搜索。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        Cancel(ref _lookupCts);
        ProductCandidates.Clear(); SelectedCandidate = null;
        if (_disposed || !_user.HasPermission(PermissionKeys.ProductsProductView) || string.IsNullOrWhiteSpace(keyword)) { IsLookupBusy = false; return; }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _lookupCts = cts; IsLookupBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _productsApi.SearchLookupAsync(new ProductLookupRequest
            { Keyword = keyword.Trim(), ExcludeProductId = SelectedProduct?.Uid ?? Guid.Empty, Limit = 20 }, cts.Token), cts.Token, "搜索子件失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_lookupCts, cts)) return;
            if (result.Success && result.Data is not null) foreach (var dto in result.Data) ProductCandidates.Add(dto);
            else BomStatusText = result.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception exception) { if (!_disposed && ReferenceEquals(_lookupCts, cts)) await HandleExceptionAsync(exception); }
        finally { if (ReferenceEquals(_lookupCts, cts)) IsLookupBusy = false; }
    }
    /// <summary>一次接纳完整子件身份及展示字段，不能只修改 SKU 字符串。</summary>
    [RelayCommand]
    private void CommitProduct(BomLineModel? line)
    {
        if (line is null || SelectedCandidate is null || !CanEditLine(line) || !CanLookupProducts) return;
        SelectedCandidate.Adapt(line); SelectedCandidate = null;
    }

    /// <summary>按行真实状态分别检查创建/编辑权限，结果未知时只允许核实。</summary>
    public bool CanEditLine(BomLineModel line) => CanEditComponents && !_unknownSave && _user.HasPermission(line.IsNew ? PermissionKeys.ProductsBomCreate : PermissionKeys.ProductsBomEdit);
    private bool CanAdd() => IsDetailReady && !_unknownSave && _user.HasPermission(PermissionKeys.ProductsBomCreate) && _user.HasPermission(PermissionKeys.ProductsProductView);
    private bool CanDelete() => IsDetailReady && !_unknownSave && SelectedLine is { } line && (line.IsNew || _user.HasPermission(PermissionKeys.ProductsBomDelete));
    private bool CanSave() => IsDetailReady && !_unknownSave && HasUnsavedChanges
        && (Boms.AddedItems.Count == 0 || _user.HasPermission(PermissionKeys.ProductsBomCreate))
        && (Boms.ModifiedItems.Count == 0 || _user.HasPermission(PermissionKeys.ProductsBomEdit))
        && (Boms.DeletedItems.Count == 0 || _user.HasPermission(PermissionKeys.ProductsBomDelete));
    private bool CanReject() => CanSelectProduct && !IsLoadingBom && HasUnsavedChanges;
    private bool CanBind() => CanSelectProduct && !IsLoadingBom && !_unknownSave && SelectedProduct is { IsBound: false } && _user.HasPermission(PermissionKeys.ProductsProductEdit);
    private bool CanCopy() => IsDetailReady && !_unknownSave && !HasUnsavedChanges && Boms.Items.Count == 0 && _user.HasPermission(PermissionKeys.ProductsBomCreate);
    private bool CanReadStructure() => IsDetailReady;

    private BomProductSearchRequest CreateRequest(int page) => new()
    {
        Keyword = Keyword.Trim(), PageIndex = page, PageSize = PageSize,
        IsBound = BindingFilterIndex == 0 ? null : BindingFilterIndex == 1,
        CategoryIds = FilterCategories.Select(item => item.Uid).Distinct().ToList(),
        ColorIds = FilterColors.Select(item => item.Uid).Distinct().ToList()
    };
    /// <summary>新页成功后才应用条件；失败恢复上一成功快照，旧请求不能恢复新请求的输入。</summary>
    private async Task<bool> LoadPageCoreAsync(BomProductSearchRequest request)
    {
        if (_disposed) return false;
        Cancel(ref _detailCts); Cancel(ref _lookupCts); IsLoadingBom = false; IsLookupBusy = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var old = Interlocked.Exchange(ref _pageCts, cts); old?.Cancel(); old?.Dispose();
        IsQuerying = true;
        var selectedId = SelectedProduct?.Uid;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchProductsAsync(request, cts.Token), cts.Token, "查询 BOM 成品失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_pageCts, cts)) return false;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message; RestoreFilters(); return false; }
            var rows = result.Data.Items.Select(dto => dto.Adapt<BomProductItemModel>()).ToArray();
            var selection = rows.FirstOrDefault(item => item.Uid == selectedId) ?? rows.FirstOrDefault();
            // 组成也成功读取后才替换主表，刷新失败不能丢失已确认放弃但尚未替换的输入。
            if (selection is not null && !await LoadBomCoreAsync(selection))
            {
                if (!_disposed && !cts.IsCancellationRequested && ReferenceEquals(_pageCts, cts)) RestoreFilters();
                return false;
            }
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_pageCts, cts)) return false;
            IsApplyingSelection = true;
            try
            {
                Products.Clear(); foreach (var row in rows) Products.Add(row);
                SelectedProduct = selection;
                if (selection is null) { Boms.LoadFromDtos([]); SelectedLine = null; _loadedProductId = null; }
                TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex; PageSize = result.Data.PageSize;
                _applied = request; _unknownSave = false; ValidationSummary = null;
                ResultSummary = $"共 {TotalCount} 个成品，本页 {rows.Length} 个。";
            }
            finally { IsApplyingSelection = false; }
            if (selection is null) BomStatusText = "没有符合条件的成品。";
            return true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return false; }
        catch (Exception exception) { if (!_disposed && ReferenceEquals(_pageCts, cts)) { RestoreFilters(); await HandleExceptionAsync(exception); } return false; }
        finally { if (ReferenceEquals(_pageCts, cts)) { IsQuerying = false; NotifyCapabilities(); } }
    }
    /// <summary>详情先在请求局部读取，成功且身份有效后再原子接纳；失败保留原选择和编辑。</summary>
    private async Task<bool> LoadBomCoreAsync(BomProductItemModel product)
    {
        Cancel(ref _lookupCts); ProductCandidates.Clear(); SelectedCandidate = null; IsLookupBusy = false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cts.Token;
        var old = Interlocked.Exchange(ref _detailCts, cts); old?.Cancel(); old?.Dispose();
        IsLoadingBom = true;
        try
        {
            List<BomDto> rows = [];
            BomNumberInfoDto? summary = null;
            if (product.IsBound)
            {
                var result = await _runner.RunAsync(() => _api.GetBomByBomNo(product.BomNo, token), token, "加载 BOM 失败");
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_detailCts, cts)) return false;
                if (!result.Success || result.Data is null) { BomStatusText = result.Message; RestoreSelection(); return false; }
                rows = result.Data;
                var info = await _runner.RunAsync(() => _api.GetNumberInfoAsync(product.BomNo, token), token, "读取共享摘要失败");
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_detailCts, cts)) return false;
                if (!info.Success || info.Data is null) { BomStatusText = info.Message; RestoreSelection(); return false; }
                summary = info.Data;
            }
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_detailCts, cts)) return false;
            IsApplyingSelection = true;
            try
            {
                SelectedProduct = product; Boms.LoadFromDtos(rows); SelectedLine = Boms.Items.FirstOrDefault();
                _loadedProductId = product.Uid; _unknownSave = false; ValidationSummary = null;
                product.ComponentCount = rows.Count;
                if (summary is not null) product.SharedProductCount = summary.BoundProductCount;
                BomStatusText = !product.IsBound ? $"{product.Sku} 待建 BOM，请建立或绑定。"
                    : $"BOM {product.BomNo} · {rows.Count} 项组成 · {product.SharedProductCount} 个共享产品"
                        + (rows.Count == 0 ? " · 待完善" : string.Empty);
            }
            finally { IsApplyingSelection = false; }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception) { if (!_disposed && ReferenceEquals(_detailCts, cts)) { RestoreSelection(); await HandleExceptionAsync(exception); } return false; }
        finally { if (ReferenceEquals(_detailCts, cts)) { IsLoadingBom = false; NotifyCapabilities(); } }
    }

    private bool CommitEdits() => CommitPendingEdits?.Invoke() ?? true;
    private void RestoreSelection() { IsApplyingSelection = true; try { OnPropertyChanged(nameof(SelectedProduct)); } finally { IsApplyingSelection = false; } }
    private void RestoreFilters()
    {
        if (_applied is null) return;
        Keyword = _applied.Keyword ?? string.Empty; BindingFilterIndex = _applied.IsBound is null ? 0 : _applied.IsBound.Value ? 1 : 2;
        PageIndex = _applied.PageIndex; PageSize = _applied.PageSize;
        FilterCategories.Clear(); foreach (var category in ReferenceDataCache.ProductCategories.Where(item => _applied.CategoryIds.Contains(item.Uid))) FilterCategories.Add(category);
        FilterColors.Clear(); foreach (var color in ReferenceDataCache.ProductColors.Where(item => _applied.ColorIds.Contains(item.Uid))) FilterColors.Add(color);
    }
    /// <summary>统一处理替换编辑上下文；同一时刻只有一个确认弹窗，写命令期间拒绝离开。</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_disposed || IsBusy || IsPrompting || !CommitEdits()) return false;
        if (!HasUnsavedChanges) return true;
        IsPrompting = true;
        try { return await _dialogs.ConfirmAsync("放弃 BOM 修改", "当前 BOM 有未保存修改，继续将放弃这些修改。", "继续", cancellationToken: _lifetime.Token); }
        finally { IsPrompting = false; }
    }
    /// <inheritdoc />
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return !IsPrompting && !IsBusy && await ConfirmDiscardAsync();
    }
    private void OnLinesChanged(object? sender, ListStateChangedEventArgs<BomLineModel> e) { IsDirty = Boms.HasChanges; OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCapabilities(); }
    private void OnPermissionsChanged(object? sender, EventArgs e) => NotifyCapabilities();
    private void OnPageStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsQuerying) or nameof(IsLoadingBom) or nameof(IsPrompting) or nameof(SelectedProduct) or nameof(SelectedLine)) NotifyCapabilities();
    }
    private void NotifyCapabilities()
    {
        OnPropertyChanged(nameof(CanSelectProduct)); OnPropertyChanged(nameof(IsDetailReady)); OnPropertyChanged(nameof(CanEditComponents)); OnPropertyChanged(nameof(IsSaveOutcomeUnknown));
        OnPropertyChanged(nameof(CanLookupProducts));
        AddItemCommand.NotifyCanExecuteChanged(); DeleteItemCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        SelectProductCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged(); BindCommand.NotifyCanExecuteChanged(); CopyCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged(); ShowTreeCommand.NotifyCanExecuteChanged();
    }
    /// <summary>未知程序错误只显示稳定业务反馈，技术细节进入客户端日志。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        _logger.LogError(exception, "BOM 管理操作失败。");
        if (!_disposed) { ValidationSummary = "BOM 操作失败，请稍后重试。"; _messages.ShowError("BOM 操作失败", ValidationSummary); }
        return Task.CompletedTask;
    }
    /// <summary>取消页面读取并解除订阅，不取消已经发出的组成写入。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Cancel(ref _pageCts); Cancel(ref _detailCts); Cancel(ref _lookupCts);
        Boms.ListStateChanged -= OnLinesChanged; PropertyChanged -= OnPageStateChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
        CommitPendingEdits = null; _lifetime.Dispose();
    }
    private static void Cancel(ref CancellationTokenSource? source) { var old = Interlocked.Exchange(ref source, null); old?.Cancel(); old?.Dispose(); }
}
