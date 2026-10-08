using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;

/// <summary>流量测试组列表；分页只读，重复打开已有组仅激活其详情页签。</summary>
public sealed partial class FlowManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    /// <summary>品质管理菜单中的稳定路由。</summary>
    public const string RouteKey = "quality-control-flows";
    private readonly IFlowApi _api;
    private readonly IProductApi _products;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _user;
    private CancellationTokenSource? _query;
    private CancellationTokenSource? _productQuery;
    private bool _initialized;
    private bool _disposed;

    /// <summary>接入现有 API、导航和权限服务，查询与物料搜索各自拥有请求身份。</summary>
    public FlowManagerViewModel(IFlowApi api, IProductApi products, IApiCallRunner runner,
        INavigationService navigation, ICurrentUserService user)
    {
        _api = api; _products = products; _runner = runner; _navigation = navigation; _user = user;
        Title = "流量管理";
        _user.CurrentUserChanged += OnUserChanged;
        foreach (var command in new IRelayCommand[] { SearchCommand, RefreshCommand, CreateCommand, OpenCommand,
            PreviousPageCommand, NextPageCommand, ClearMaterialCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>当前页只读记录，直接消费列表查询投影，不作为编辑输入。</summary>
    public ObservableCollection<FlowListItemDto> Groups { get; } = [];
    /// <summary>当前物料搜索候选，仅属于本页面。</summary>
    public ObservableCollection<ProductLookupDto> MaterialOptions { get; } = [];
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial ProductLookupDto? SelectedMaterial { get; set; }
    [ObservableProperty] public partial DateTime? DateFrom { get; set; }
    [ObservableProperty] public partial DateTime? DateTo { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(OpenCommand))] public partial FlowListItemDto? SelectedGroup { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未查询测试组。";
    [ObservableProperty] public partial bool IsMaterialSearchBusy { get; set; }
    /// <summary>分页规模固定为 20，避免一次加载所有测试历史。</summary>
    public int PageSize => 20;
    /// <summary>列表和深链入口均按查看权限控制。</summary>
    public bool HasAccess => !_disposed && _user.HasPermission(PermissionKeys.QualityControlFlowView);
    /// <summary>展示总量及当前位置。</summary>
    public string PageStatus => $"第 {PageIndex} / {Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize))} 页，共 {TotalCount} 组";

    /// <summary>首次打开初始化；重复激活保留筛选和列表。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        await LoadPageAsync(1);
    }

    /// <summary>新筛选查询替代旧请求；不在 setter 发出网络请求。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => LoadPageAsync(1);
    /// <summary>显式刷新当前页，接纳其他详情页保存后的变化。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery), AllowConcurrentExecutions = true)]
    private Task RefreshAsync() => LoadPageAsync(PageIndex);
    /// <summary>加载上一页。</summary>
    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private Task PreviousPageAsync() => LoadPageAsync(PageIndex - 1);
    /// <summary>加载下一页。</summary>
    [RelayCommand(CanExecute = nameof(CanNext))]
    private Task NextPageAsync() => LoadPageAsync(PageIndex + 1);
    /// <summary>新建每次使用独立临时页签身份，避免覆盖其他未保存测试组。</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => _navigation.OpenDocumentAsync(FlowDetailViewModel.RouteKey,
        $"flow:new:{Guid.NewGuid():N}", new FlowOpenRequest());
    /// <summary>同一已保存测试组使用带类型的页签身份。</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync() => _navigation.OpenDocumentAsync(FlowDetailViewModel.RouteKey,
        $"flow:{SelectedGroup!.Uid:N}", new FlowOpenRequest { HeaderId = SelectedGroup.Uid });
    /// <summary>明确清除物料筛选，不影响独立的 SKU 输入。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private void ClearMaterial() => SelectedMaterial = null;

    private bool CanQuery() => HasAccess;
    private bool CanPrevious() => HasAccess && IsNotBusy && PageIndex > 1;
    private bool CanNext() => HasAccess && IsNotBusy && (long)PageIndex * PageSize < TotalCount;
    private bool CanCreate() => HasAccess && IsNotBusy && _user.HasPermission(PermissionKeys.QualityControlFlowCreate);
    private bool CanOpen() => HasAccess && IsNotBusy && SelectedGroup is not null;

    /// <summary>把一次分页请求的筛选、页码和令牌固定下来；迟到响应不得改变当前页。</summary>
    private async Task LoadPageAsync(int page)
    {
        if (!HasAccess) { ResultSummary = "当前账号没有流量查看权限。"; return; }
        if (DateFrom?.Date > DateTo?.Date) { ValidationSummary = "测试日期起不能晚于日期止。"; return; }
        _query?.Cancel();
        var cts = new CancellationTokenSource();
        _query = cts;
        var request = new FlowSearchRequest { MaterialId = SelectedMaterial?.Uid, SkuSnapshot = Sku.Trim(),
            TestDateFrom = DateFrom?.Date, TestDateTo = DateTo?.Date, PageIndex = Math.Max(1, page), PageSize = PageSize };
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await _runner.RunAsync(() => _api.GetPagedAsync(request, cts.Token), cts.Token, "查询流量测试组失败");
                if (!HasAccess || cts.IsCancellationRequested || !ReferenceEquals(_query, cts)) return;
                if (!result.Success || result.Data is null) { ResultSummary = result.Error?.Message ?? result.Message; return; }
                Groups.Clear();
                foreach (var item in result.Data.Items) Groups.Add(item);
                PageIndex = result.Data.PageIndex; TotalCount = result.Data.TotalCount;
                SelectedGroup = Groups.FirstOrDefault();
                ValidationSummary = null;
                ResultSummary = Groups.Count == 0 ? "没有符合条件的测试组。" : $"本页显示 {Groups.Count} 组。";
            }, "正在查询测试组…", false);
        }
        finally
        {
            if (ReferenceEquals(_query, cts)) _query = null;
            cts.Dispose();
        }
    }

    /// <summary>物料选择器远程搜索只接纳当前请求，取消不清空已经显示的选择。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchMaterialsAsync(string? keyword)
    {
        if (!HasAccess) return;
        _productQuery?.Cancel();
        var cts = new CancellationTokenSource(); _productQuery = cts;
        IsMaterialSearchBusy = true;
        try
        {
            var result = await _runner.RunAsync(() => _products.SearchLookupAsync(
                new ProductLookupRequest { Keyword = keyword?.Trim() ?? "", Limit = 20 }, cts.Token), cts.Token, "查询物料失败");
            if (!HasAccess || cts.IsCancellationRequested || !ReferenceEquals(_productQuery, cts)) return;
            if (!result.Success) { ResultSummary = result.Error?.Message ?? result.Message; return; }
            MaterialOptions.Clear();
            foreach (var item in result.Data ?? []) MaterialOptions.Add(item);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_productQuery, cts)) { _productQuery = null; if (!_disposed) IsMaterialSearchBusy = false; }
            cts.Dispose();
        }
    }

    /// <summary>权限撤回时取消请求并清除列表；恢复权限后由显式刷新重新加载。</summary>
    private void OnUserChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(HasAccess));
        if (!HasAccess) { _query?.Cancel(); _productQuery?.Cancel(); Groups.Clear(); MaterialOptions.Clear(); SelectedGroup = null; }
        foreach (var command in new IRelayCommand[] { SearchCommand, RefreshCommand, CreateCommand, OpenCommand,
            PreviousPageCommand, NextPageCommand, ClearMaterialCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>释放时取消查询和权限订阅，令牌由各请求的 finally 释放。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _query?.Cancel(); _productQuery?.Cancel();
        _user.CurrentUserChanged -= OnUserChanged;
    }
    /// <summary>未知页面错误只给出通用反馈，不暴露内部堆栈或地址。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        if (!_disposed) ValidationSummary = "操作失败，请稍后重试。";
        return Task.CompletedTask;
    }
}

