using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;

/// <summary>售后管理页模型；负责单头分页筛选、金额状态展示及进入详情。</summary>
public sealed partial class AfterSalesManagerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "quality-control-after-sales";
    private readonly IAfterSalesApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private CancellationTokenSource? _queryCancellation;
    private bool _initialized;
    private bool _disposed;

    /// <summary>创建售后管理页并接入 API、导航和当前用户权限。</summary>
    public AfterSalesManagerViewModel(IAfterSalesApi api, IApiCallRunner runner, INavigationService navigation, ICurrentUserService currentUser)
    {
        _api = api; _runner = runner; _navigation = navigation; _currentUser = currentUser;
        Title = "售后服务管理";
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(CreateCommand); RegisterBusyAwareCommand(OpenDetailCommand);
        RegisterBusyAwareCommand(PageChangeCommand); RegisterBusyAwareCommand(PreviousPageCommand); RegisterBusyAwareCommand(NextPageCommand);
    }

    public ObservableCollection<AfterSalesManagerRowModel> Orders { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> CustomerOptions { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> SupplierOptions { get; } = [];
    public IReadOnlyList<AfterSalesFilterOption<DocumentStatusEnum?>> DocumentStatusOptions { get; } =
    [
        new(null, "全部生命周期"), new(DocumentStatusEnum.Open, "开放"),
        new(DocumentStatusEnum.Closed, "已结案"), new(DocumentStatusEnum.Voided, "已作废")
    ];
    public IReadOnlyList<AfterSalesFilterOption<ConfirmationStatusEnum?>> ConfirmationOptions { get; } =
    [
        new(null, "全部确认状态"), new(ConfirmationStatusEnum.待确认, "待确认"),
        new(ConfirmationStatusEnum.已确认, "已确认")
    ];
    public bool HasAccess => _currentUser.HasPermission(PermissionKeys.ModuleQualityControlAccess);
    public int TotalPages => TotalCount <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string PageStatus => $"第 {PageIndex} / {TotalPages} 页";

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial AfterSalesLookupDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial AfterSalesLookupDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial AfterSalesFilterOption<DocumentStatusEnum?>? SelectedDocumentStatus { get; set; }
    [ObservableProperty] public partial AfterSalesFilterOption<ConfirmationStatusEnum?>? SelectedConfirmationStatus { get; set; }
    [ObservableProperty] public partial DateTime? DateFrom { get; set; }
    [ObservableProperty] public partial DateTime? DateTo { get; set; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))] public partial AfterSalesManagerRowModel? SelectedOrder { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int TotalCount { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TotalPages))][NotifyPropertyChangedFor(nameof(PageStatus))] public partial int PageSize { get; set; } = 20;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载售后服务单。";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        SelectedDocumentStatus = DocumentStatusOptions[0];
        SelectedConfirmationStatus = ConfirmationOptions[0];
        if (!HasAccess) { ResultSummary = "当前账号没有品质管理权限。"; return; }
        await LoadLookupsAsync();
        await LoadPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)] private async Task SearchAsync() { PageIndex = 1; await LoadPageAsync(); }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task RefreshAsync() => LoadPageAsync();
    [RelayCommand] private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        PageIndex = request.NewIndex; PageSize = request.NewSize;
        return LoadPageAsync();
    }
    [RelayCommand(CanExecute = nameof(CanPreviousPage))] private async Task PreviousPageAsync() { PageIndex--; await LoadPageAsync(); }
    [RelayCommand(CanExecute = nameof(CanNextPage))] private async Task NextPageAsync() { PageIndex++; await LoadPageAsync(); }
    [RelayCommand(CanExecute = nameof(CanCreate))] private Task CreateAsync() => _navigation.OpenDocumentAsync(
        AfterSalesDetailViewModel.RouteKey, $"after-sales:new:{Guid.NewGuid():N}", new AfterSalesDocumentOpenRequest());
    [RelayCommand(CanExecute = nameof(CanOpenDetail))] private Task OpenDetailAsync() => _navigation.OpenDocumentAsync(
        AfterSalesDetailViewModel.RouteKey, $"after-sales:{SelectedOrder!.Uid:N}",
        new AfterSalesDocumentOpenRequest { AfterSalesId = SelectedOrder.Uid });
    private bool CanOpenDetail() => HasAccess && IsNotBusy && SelectedOrder is not null;
    private bool CanCreate() => HasAccess && IsNotBusy;
    private bool CanPreviousPage() => HasAccess && IsNotBusy && PageIndex > 1;
    private bool CanNextPage() => HasAccess && IsNotBusy && PageIndex < TotalPages;

    // 管理查询允许新请求替代旧请求；取消和请求身份共同阻止旧结果回写。
    private async Task LoadPageAsync()
    {
        if (!HasAccess || _disposed) return;
        _queryCancellation?.Cancel(); _queryCancellation?.Dispose();
        var cts = new CancellationTokenSource(); _queryCancellation = cts;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(new AfterSalesSearchRequest
            {
                Keyword = Keyword.Trim(), CustomerId = SelectedCustomer?.Uid,
                ResponsibleSupplierId = SelectedSupplier?.Uid, DocumentStatus = SelectedDocumentStatus?.Value,
                ConfirmationStatus = SelectedConfirmationStatus?.Value, DateFrom = DateFrom, DateTo = DateTo,
                PageIndex = PageIndex, PageSize = PageSize
            }, cts.Token), cts.Token, "查询售后服务单失败");
            if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_queryCancellation, cts)) return;
            if (!result.Success || result.Data is null)
            {
                ResultSummary = result.Error?.Message ?? result.Message; return;
            }
            Orders.Clear();
            foreach (var item in result.Data.Items) Orders.Add(item.Adapt<AfterSalesManagerRowModel>());
            TotalCount = result.Data.TotalCount; PageIndex = result.Data.PageIndex;
            PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged();
            SelectedOrder = Orders.FirstOrDefault();
            ResultSummary = $"共 {TotalCount} 张售后服务单，本页显示 {Orders.Count} 张。";
        }, "正在查询售后服务单...", false);
        if (ReferenceEquals(_queryCancellation, cts)) _queryCancellation = null;
        cts.Dispose();
    }

    // 管理筛选候选只加载窄身份和显示名，不缓存用户或完整主数据。
    private async Task LoadLookupsAsync()
    {
        var customers = await _runner.RunAsync(() => _api.LookupAsync("customers", new AfterSalesLookupRequest { PageIndex = 1, PageSize = 200 }), "加载客户候选失败");
        var suppliers = await _runner.RunAsync(() => _api.LookupAsync("suppliers", new AfterSalesLookupRequest { PageIndex = 1, PageSize = 200 }), "加载责任方候选失败");
        if (customers.Success && customers.Data is not null) foreach (var item in customers.Data.Items) CustomerOptions.Add(item);
        if (suppliers.Success && suppliers.Data is not null) foreach (var item in suppliers.Data.Items) SupplierOptions.Add(item);
    }

    /// <summary>取消尚未完成的列表请求并阻止页面释放后回写。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queryCancellation?.Cancel(); _queryCancellation?.Dispose(); _queryCancellation = null;
    }
}
