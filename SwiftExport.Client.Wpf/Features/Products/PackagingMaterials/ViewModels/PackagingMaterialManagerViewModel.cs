using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Mapster;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.PackagingMaterials.ViewModels;

/// <summary>包材档案专页：复用物料身份、批量编辑模型和 API；不维护被包装物料的包装关系。</summary>
public sealed partial class PackagingMaterialManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    /// <summary>包材管理的单实例导航路由，复用普通物料访问权限。</summary>
    public const string RouteKey = "packaging-materials";
    private readonly IProductApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private readonly INavigationService _navigation;
    private readonly IProductImageUploadDialogService _images;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _query;
    private bool _initialized, _disposed;

    /// <summary>注入既有物料、缓存及 UI 服务；列表和详情通过身份消息协作，不共享草稿。</summary>
    public PackagingMaterialManagerViewModel(IProductApi api, IApiCallRunner runner, IDialogService dialogs,
        ICurrentUserService user, INavigationService navigation, IReferenceDataCacheService referenceDataCache,
        IProductImageUploadDialogService images)
    {
        _api = api; _runner = runner; _dialogs = dialogs; _user = user; _navigation = navigation;
        ReferenceDataCache = referenceDataCache; _images = images; Title = DocumentTitle;
        Products.PropertyChanged += OnListChanged;
        _user.CurrentUserChanged += OnPermissionsChanged;
        PropertyChanged += OnPageStateChanged;
        InitializeCheckSelection();
        WeakReferenceMessenger.Default.Register<PackagingMaterialManagerViewModel, ProductDetailSavedMessage>(this,
            static (recipient, message) => recipient.ObserveDetailSaved(message.ProductId));
        foreach (var command in new IRelayCommand[] { SearchCommand, ResetCommand, RefreshCommand, AddRowCommand,
                     DeleteSelectedCommand, SaveCommand, RejectCommand, PageChangeCommand, OpenDetailCommand, UploadImagesCommand })
            RegisterBusyAwareCommand(command);
    }

    /// <summary>复用普通物料编辑列表及删除池，不另建包材业务模型。</summary>
    public ProductListModel Products { get; } = new();
    /// <summary>单位、供应商等现有会话缓存。</summary>
    public IReferenceDataCacheService ReferenceDataCache { get; }
    /// <summary>本地筛选后的供应商建议，由现有选择器消费。</summary>
    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    /// <summary>当前启用单位候选。</summary>
    public IReadOnlyList<SysDictDataDto> UnitOptions => ReferenceDataCache.GetDictionaryValues(DictCodes.Unit);
    /// <summary>沿用物料供货方式候选。</summary>
    public IReadOnlyList<string> SupplyTypes => SupplyTypeEnums.SupplyTypes;
    /// <summary>原生编辑提交桥，由 View 挂接并在卸载时解除。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>导航标题。</summary>
    public string DocumentTitle => "包材管理";
    /// <summary>真实列表草稿的关闭保护状态。</summary>
    public bool HasUnsavedChanges => Products.HasChanges;
    /// <summary>新增权限；与编辑现有包材的权限分开。</summary>
    public bool CanCreate => _user.HasPermission(PermissionKeys.ProductsProductCreate);
    /// <summary>已有包材编辑权限。</summary>
    public bool CanEdit => _user.HasPermission(PermissionKeys.ProductsProductEdit);
    /// <summary>表格写入期间冻结，行级权限由原生 BeginningEdit 再复核。</summary>
    public bool IsGridReadOnly => _disposed || IsBusy || IsQueryBusy || !CanRead()
        || !(CanCreate || CanEdit || _user.HasPermission(PermissionKeys.ProductsProductDelete));
    /// <summary>页码与总量提示。</summary>
    public string PageStatus => $"第 {PageIndex} 页，共 {TotalCount} 条";

    /// <summary>编号、描述和规格关键词。</summary>
    [ObservableProperty] public partial string Keyword { get; set; } = "";
    /// <summary>当前物料行，单元格选择模式不能触发行删除或详情操作。</summary>
    [ObservableProperty] public partial ProductLineModel? SelectedLine { get; set; }
    /// <summary>只影响选择语义，不用当前单元格冒充勾选行。</summary>
    [ObservableProperty] public partial bool IsCellSelectionMode { get; set; }
    /// <summary>只有查询成功才接纳新页码。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    /// <summary>服务端分页大小。</summary>
    [ObservableProperty] public partial int PageSize { get; set; } = 20;
    /// <summary>当前筛选总数。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    /// <summary>加载、验证或保存结果的唯一页面反馈。</summary>
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载包材档案。";
    /// <summary>供应商选择器本次确认的值。</summary>
    [ObservableProperty] public partial SupplierDto? EditSupplier { get; set; }
    /// <summary>可替代读取的独立状态，旧请求不能结束新查询的忙碌状态，也不用于中断保存。</summary>
    [ObservableProperty] public partial bool IsQueryBusy { get; private set; }

    /// <summary>首次进入幂等加载引用缓存和包材列表；关闭后不再回写。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed || !CanRead()) return;
        _initialized = true;
        IsQueryBusy = true;
        try
        {
            var cache = await ReferenceDataCache.EnsureLoadedAsync();
            if (_disposed) return;
            if (!cache.Success) ResultSummary = cache.Message;
            OnPropertyChanged(nameof(UnitOptions));
            await LoadAsync(1, PageSize);
        }
        catch (Exception exception) { _initialized = false; await HandleExceptionAsync(exception); }
        finally { if (_query is null) IsQueryBusy = false; }
    }

    /// <summary>原生编辑开始前按行的新建/既有状态复核权限。</summary>
    public bool CanEditRow(ProductLineModel row) => !IsGridReadOnly && (row.IsNew ? CanCreate : CanEdit);
    private bool CanRead() => !_disposed && _user.HasPermission(PermissionKeys.ProductsProductView);
    private bool CanReadCommand() => CanRead() && IsNotBusy && !IsQueryBusy;
    private bool CanQueryCommand() => CanRead() && IsNotBusy;
    private bool CanAdd() => CanReadCommand() && CanCreate;
    private bool CanDelete() => CanReadCommand() && !IsCellSelectionMode
        && _user.HasPermission(PermissionKeys.ProductsProductDelete) && SelectedRows().Length > 0;
    // 即使脏状态尚未因失焦提交而变化，保存入口也必须允许先提交当前单元格。
    private bool CanSave() => CanReadCommand() && (CanCreate || CanEdit || _user.HasPermission(PermissionKeys.ProductsProductDelete));
    private bool CanOpenDetail() => CanReadCommand() && !IsCellSelectionMode && SelectedLine is { IsNew: false, IsDeleted: false };
    private bool CanUploadImages() => CanReadCommand() && CanEdit;
    private ProductLineModel[] SelectedRows() => Products.CheckedCount > 0 ? Products.CheckedItems.ToArray()
        : SelectedLine is null ? [] : [SelectedLine];

    /// <summary>搜索始终附带包材类型；取消放弃时保留当前数据及页码。</summary>
    [RelayCommand(CanExecute = nameof(CanQueryCommand), AllowConcurrentExecutions = true)]
    private async Task SearchAsync() { if (await PrepareReloadAsync()) await LoadAsync(1, PageSize); }
    /// <summary>重置关键词不能清除固定包材类型。</summary>
    [RelayCommand(CanExecute = nameof(CanQueryCommand))]
    private async Task ResetAsync() { if (!await PrepareReloadAsync()) return; Keyword = ""; await LoadAsync(1, PageSize); }
    /// <summary>明确刷新前检查未保存草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanQueryCommand), AllowConcurrentExecutions = true)]
    private async Task RefreshAsync() { if (await PrepareReloadAsync()) await LoadAsync(PageIndex, PageSize); }
    /// <summary>翻页查询成功后才改变页面索引，失败和用户取消保持原页。</summary>
    [RelayCommand(CanExecute = nameof(CanQueryCommand))]
    private async Task PageChangeAsync(PageChangeRequest? request)
    { if (request is not null && await PrepareReloadAsync()) await LoadAsync(Math.Max(1, request.NewIndex), Math.Max(1, request.NewSize)); }
    /// <summary>新增使用普通 Product 身份，类型固定为包材，默认采购。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddRow()
    {
        if (!CanAdd() || !CommitEdits()) return;
        SelectedLine = Products.AddNew(new ProductDto { MaterialType = MaterialTypesEnum.包材, SupplyType = SupplyTypeEnums.采购 });
    }
    /// <summary>删除仅采用勾选行或完整行选择，确认后进入既有删除池，可在保存前撤销。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDelete() || !CommitEdits()) return;
        var rows = SelectedRows();
        if (rows.Any(row => !row.IsNew) && !await _dialogs.ConfirmAsync("删除包材", "选中包材将在保存时删除；已有包装或 BOM 引用时后端会拒绝。", "删除")) return;
        if (!CanDelete()) return;
        foreach (var row in rows) Products.Remove(row);
        SelectedLine = Products.Items.FirstOrDefault();
    }
    /// <summary>撤销前提交单元格缓冲，统一恢复行基线和删除池。</summary>
    [RelayCommand(CanExecute = nameof(CanReadCommand))]
    private void Reject()
    { if (!CanReadCommand() || !CommitEdits()) return; Products.RejectChanges(); SelectedLine = Products.Items.FirstOrDefault(); }

    /// <summary>冻结批量请求，成功且所有回执完整后接纳版本；失败、冲突或异常始终保留草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave() || !CommitEdits()) return;
        await RunBusyAsync(async () =>
        {
            var validation = await Products.ValidateAllAsync();
            if (validation is not null) { ResultSummary = validation; return; }
            if (Products.Items.Any(row => row.MaterialType != MaterialTypesEnum.包材)) { ResultSummary = "包材专页不能更改物料类型。"; return; }
            if (Products.AddedItems.Count > 0 && !CanCreate || Products.ModifiedItems.Count > 0 && !CanEdit
                || Products.DeletedItems.Count > 0 && !_user.HasPermission(PermissionKeys.ProductsProductDelete))
            { ResultSummary = "没有提交这些新增、修改或删除操作的权限，草稿已保留。"; return; }
            var request = Products.CreateSaveRequest();
            if (!request.HasChanges) { ResultSummary = "没有需要保存的变更。"; return; }
            var targets = new List<(ProductLineModel Row, ProductDto Data, SaveOperation Operation, string Key)>();
            for (var i = 0; i < request.CreateItems.Count; i++)
            { var item = request.CreateItems[i]; item.ClientKey = $"create:{i}"; item.RowIndex = Products.AddedItems[i].RowIndex; targets.Add((Products.AddedItems[i], item.Data, SaveOperation.Create, item.ClientKey)); }
            for (var i = 0; i < request.UpdateItems.Count; i++)
            { var item = request.UpdateItems[i]; item.ClientKey = $"update:{i}"; targets.Add((Products.ModifiedItems[i], item.Data, SaveOperation.Update, item.ClientKey)); }
            for (var i = 0; i < request.DeleteItems.Count; i++)
            { var item = request.DeleteItems[i]; item.ClientKey = $"delete:{i}"; targets.Add((Products.DeletedItems[i], Products.DeletedItems[i].ToDto(), SaveOperation.Delete, item.ClientKey)); }
            var identity = _user.CurrentUser;
            var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存包材失败");
            if (_disposed) return;
            if (!ReferenceEquals(identity, _user.CurrentUser) || !CanRead())
            { ResultSummary = "当前会话或权限已变化，未接纳旧会话保存回执，请核实结果。"; return; }
            var receipts = result.Data?.Items;
            if (!result.Success || receipts is null || receipts.Count != targets.Count
                || targets.Any(target => receipts.Count(receipt => receipt.ClientKey == target.Key && receipt.Operation == target.Operation
                    && receipt.Success && receipt.Uid.HasValue && receipt.Uid != Guid.Empty && receipt.NewVersion is > 0
                    && (target.Operation == SaveOperation.Create || receipt.Uid == target.Row.Uid && receipt.NewVersion > target.Data.Version)) != 1))
            { ResultSummary = result.Success ? "保存回执不完整，请核实结果；当前草稿已保留。" : result.Message; return; }
            foreach (var target in targets)
            {
                var receipt = receipts.Single(item => item.ClientKey == target.Key);
                if (target.Operation == SaveOperation.Delete) { Products.RemoveCompletely(target.Row); continue; }
                // 接纳请求快照的版本后重放期间新增的编辑，不能把晚到本地修改误当作已提交。
                var latest = target.Row.ToDto();
                target.Data.Uid = receipt.Uid!.Value; target.Data.Version = receipt.NewVersion!.Value;
                target.Row.Load(target.Data);
                latest.Uid = target.Data.Uid; latest.Version = target.Data.Version;
                latest.Adapt(target.Row);
            }
            TotalCount = Math.Max(0, TotalCount + result.Data!.AddSuccessCount - result.Data.DeleteSuccessCount);
            ResultSummary = $"包材档案已保存，共 {receipts.Count} 条变更。";
            NotifyState();
        }, "正在保存包材档案…");
    }

    /// <summary>详情沿用 Product 文档身份；先处理当前页草稿，避免两个编辑入口相互覆盖。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private async Task OpenDetailAsync()
    {
        if (!CanOpenDetail() || !await PrepareReloadAsync() || !CanOpenDetail()) return;
        // 该动作不会重载列表；显式放弃已确认的列表草稿后才交给独立详情编辑。
        Products.RejectChanges();
        await _navigation.OpenDocumentAsync(ProductDetailViewModel.RouteKey, $"product:{SelectedLine!.Uid:N}",
            new ProductDocumentOpenRequest { ProductId = SelectedLine.Uid });
    }
    /// <summary>复用既有图片上传对话框和上传权限，不创建第二条上传链路。</summary>
    [RelayCommand(CanExecute = nameof(CanUploadImages))]
    private async Task UploadImagesAsync()
    {
        if (!CanUploadImages() || !await PrepareReloadAsync()) return;
        Products.RejectChanges();
        await RunBusyAsync(_images.ShowAsync, "上传包材图片");
        if (CanReadCommand()) await LoadAsync(PageIndex, PageSize);
    }
    /// <summary>复用会话供应商缓存，在现有供应商选择器中提供本地匹配建议。</summary>
    [RelayCommand]
    private void SearchSuppliers(string? keyword)
    {
        SupplierCandidates.Clear();
        var text = keyword?.Trim() ?? "";
        foreach (var supplier in ReferenceDataCache.Suppliers.Where(item =>
                     text.Length == 0 || item.SupplierCode?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                         || item.SupplierName?.Contains(text, StringComparison.OrdinalIgnoreCase) == true).Take(30)) SupplierCandidates.Add(supplier);
    }
    /// <summary>供应商身份、编码、名称作为同一选择接纳。</summary>
    [RelayCommand]
    private void CommitSupplier(ProductLineModel? row)
    { if (row is null || !CanEditRow(row) || EditSupplier is null) return; row.DefaultSupplierId = EditSupplier.Uid; row.DefaultSupplierCode = EditSupplier.SupplierCode; row.SupplierName = EditSupplier.SupplierName; }
    /// <summary>明确清除供应商，避免只改显示文本留下旧身份。</summary>
    [RelayCommand]
    private void ClearSupplier(ProductLineModel? row)
    { if (row is null || !CanEditRow(row)) return; row.DefaultSupplierId = null; row.DefaultSupplierCode = ""; row.SupplierName = ""; }

    /// <summary>查询取消旧请求并验证请求身份和页面存活，只有成功结果才能替换列表。</summary>
    private async Task LoadAsync(int index, int size)
    {
        if (!CanQueryCommand()) return;
        _query?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _query = query;
        IsQueryBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetPagedAsync(new ProductSearchRequest { Keyword = Keyword?.Trim(),
                MaterialTypeList = [MaterialTypesEnum.包材], PageIndex = index, PageSize = size }, query.Token), query.Token, "查询包材失败");
            if (_disposed || query.IsCancellationRequested || !ReferenceEquals(_query, query) || !CanRead()) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message; return; }
            if (result.Data.Items.Any(item => item.MaterialType != MaterialTypesEnum.包材))
            { ResultSummary = "查询返回了非包材记录，未替换当前列表。"; return; }
            Products.LoadFromDtos(result.Data.Items);
            SelectedLine = Products.Items.FirstOrDefault(); TotalCount = result.Data.TotalCount;
            PageIndex = result.Data.PageIndex; PageSize = result.Data.PageSize;
            ResultSummary = $"已加载 {Products.Items.Count} 条包材档案。";
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception exception) { if (!_disposed && ReferenceEquals(_query, query)) await HandleExceptionAsync(exception); }
        finally
        {
            if (ReferenceEquals(_query, query)) { _query = null; IsQueryBusy = false; }
        }
    }
    /// <summary>放弃只在用户确认且下一次查询成功后发生；提交失败或取消确认均保留编辑。</summary>
    private async Task<bool> PrepareReloadAsync() => CanQueryCommand() && CommitEdits()
        && (!HasUnsavedChanges || await _dialogs.ConfirmAsync("放弃未保存变更", "包材列表有未保存修改，继续将放弃这些修改。", "继续")) && CanQueryCommand();
    private bool CommitEdits()
    { if (CommitPendingEdits?.Invoke() != false) return true; ResultSummary = "当前单元格无法提交，请先修正输入。"; return false; }
    /// <summary>关闭期间拒绝中断保存，先采集当前编辑，再提示未保存草稿。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => IsBusy ? Task.FromResult(false)
        : CommitEdits() && !HasUnsavedChanges ? Task.FromResult(true)
        : _dialogs.ConfirmAsync("关闭包材管理", "当前包材编辑尚未保存，确定放弃并关闭吗？", "关闭", cancellationToken: cancellationToken);
    /// <summary>详情保存后仅在列表没有草稿时刷新；有草稿时提示而不覆盖。</summary>
    private void ObserveDetailSaved(Guid id)
    {
        if (_disposed || !Products.Items.Any(row => row.Uid == id)) return;
        if (!CommitEdits() || HasUnsavedChanges || IsBusy) { ResultSummary = "物料详情已保存，请处理当前草稿后刷新包材列表。"; return; }
        _ = LoadAsync(PageIndex, PageSize);
    }
    private void OnListChanged(object? sender, PropertyChangedEventArgs args) => NotifyState();
    private void OnPermissionsChanged(object? sender, EventArgs args) { _query?.Cancel(); NotifyState(); }
    private void OnPageStateChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(IsBusy) or nameof(IsQueryBusy) or nameof(SelectedLine) or nameof(IsCellSelectionMode)) NotifyState(); }
    /// <summary>集中更新包材草稿、勾选聚合、权限和命令状态，避免菜单持有旧资格。</summary>
    private void NotifyState()
    {
        RefreshCheckState();
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(IsGridReadOnly));
        foreach (var command in new IRelayCommand[] { SaveCommand, AddRowCommand, DeleteSelectedCommand, OpenDetailCommand, SearchCommand, RefreshCommand, ResetCommand, PageChangeCommand, UploadImagesCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>未知异常保留草稿并给出稳定提示，技术详情只进入诊断日志。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { System.Diagnostics.Trace.TraceError("包材档案操作异常：{0}", exception); if (!_disposed) ResultSummary = "操作未完成，当前编辑已保留。"; return Task.CompletedTask; }
    /// <summary>释放时取消查询及全部订阅，不以取消读取中断已经提交的写命令。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; DetachCheckSelection(); _lifetime.Cancel(); _query?.Cancel(); _lifetime.Dispose();
        Products.PropertyChanged -= OnListChanged; _user.CurrentUserChanged -= OnPermissionsChanged;
        PropertyChanged -= OnPageStateChanged; WeakReferenceMessenger.Default.Unregister<ProductDetailSavedMessage>(this); CommitPendingEdits = null;
    }
}
