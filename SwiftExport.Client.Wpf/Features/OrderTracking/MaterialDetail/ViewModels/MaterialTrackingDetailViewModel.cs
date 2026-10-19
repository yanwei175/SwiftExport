using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels;

/// <summary>以稳定物料需求身份展示快照、采购承接及整行收货，各区域独立读取，不分摊共享实收。</summary>
public sealed partial class MaterialTrackingDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>独立物料跟踪隐藏路由。</summary>
    public const string RouteKey = "material-tracking-detail";
    private readonly ICurrentUserService _user;
    private readonly INavigationService _navigation;
    private bool _disposed;

    /// <summary>注入物料范围只读接口，复用区域状态及独立权限保护。</summary>
    public MaterialTrackingDetailViewModel(IOrderTrackingApi api, IApiCallRunner runner, INavigationService navigation, ICurrentUserService currentUser)
    {
        _user = currentUser; _navigation = navigation; Title = "物料跟踪详情"; IsReadOnly = true;
        Header = new(runner, currentUser, [PermissionKeys.DemandPlanningMaterialView], api.GetMaterialDetailAsync,
            _ => false, x => x.Material?.ReviewReason,
            x => x.Material?.NetDemandId == x.NetDemandId ? x.NetDemandId : Guid.Empty, "物料需求");
        Purchases = new(runner, currentUser, [PermissionKeys.DemandPlanningMaterialView, PermissionKeys.ProcurementOrderRead], api.GetMaterialPurchasesAsync,
            x => x.Items.Count == 0, x => Reviews(x.Items.Select(row => row.HasPurchaseTarget ? row.ReviewReason : "采购目标缺失，待核实。")
                .Append(x.ReviewReason).Append(x.AssociatedPurchaseQty is null || x.RemainingToPurchaseQty is null ? "采购承接数量待核实。" : null)),
            x => x.NetDemandId, "物料需求");
        Receipts = new(runner, currentUser, [PermissionKeys.DemandPlanningMaterialView, PermissionKeys.ProcurementOrderRead, PermissionKeys.InventoryInboundView], api.GetMaterialReceiptsAsync,
            x => x.Items.Count == 0 && x.PurchaseLines.Count == 0,
            x => Reviews(x.Items.Select(row => row.ReviewReason).Concat(x.PurchaseLines.Select(row => row.ReviewReason))
                .Append(x.ReviewReason).Append(x.PurchaseLines.Any(row => row.ReceivedQty is null || row.RemainingQty is null) ? "采购整行收货进度待核实。" : null)),
            x => x.NetDemandId, "物料需求");
        Header.PropertyChanged += OnHeaderChanged;
        _user.CurrentUserChanged += OnUserChanged;
    }

    /// <summary>物料快照及回退历史，不要求采购或收货权限。</summary>
    public DemandTrackingSectionViewModel<MaterialTrackingDetailDto> Header { get; }
    /// <summary>本物料可信采购承接量及活动、历史关联。</summary>
    public DemandTrackingSectionViewModel<MaterialTrackingPurchaseSectionDto> Purchases { get; }
    /// <summary>本物料关联采购行的整行进度及证据。</summary>
    public DemandTrackingSectionViewModel<DemandTrackingReceiptSectionDto> Receipts { get; }
    [ObservableProperty] public partial int SelectedSectionIndex { get; set; }
    /// <summary>入口需要跟踪及物料查看权限。</summary>
    public bool CanView => !_disposed && _user.HasPermission(PermissionKeys.OrderTrackingView) && _user.HasPermission(PermissionKeys.DemandPlanningMaterialView);
    /// <inheritdoc />
    public string DocumentTitle => Header.Data is { } header ? $"物料 · {header.Material.Sku} / 修订 {header.Material.DecompositionRevision}" : "物料跟踪详情";
    /// <inheritdoc />
    public bool HasUnsavedChanges => false;
    /// <inheritdoc />
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>仅接受 NetDemandId，不按型号、来源或根物料身份合并页签。</summary>
    public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not Guid id || id == Guid.Empty) throw new ArgumentException("缺少有效物料需求身份。", nameof(parameter));
        Header.Reset(id); Purchases.Reset(id); Receipts.Reset(id);
        return Task.WhenAll(Header.EnsureLoadedAsync(cancellationToken), LoadSelectedSectionCoreAsync(cancellationToken));
    }

    /// <summary>区域首次激活才读取，切换区域不取消其他区域。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadSelectedSectionAsync() => LoadSelectedSectionCoreAsync(CancellationToken.None);
    private Task LoadSelectedSectionCoreAsync(CancellationToken token) => SelectedSectionIndex switch
    { 1 => Purchases.EnsureLoadedAsync(token), 2 => Receipts.EnsureLoadedAsync(token), _ => Header.EnsureLoadedAsync(token) };

    /// <summary>返回保活总览，不重建来源筛选或当前完整子表。</summary>
    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ReturnToOverviewAsync() => CanView ? _navigation.OpenAsync(OrderTrackingOverviewViewModel.RouteKey) : Task.CompletedTask;
    private bool CanReturnToDemand() => CanView && Header.Data is { GrossDemandId: var id } && id != Guid.Empty;
    /// <summary>按所属成品需求稳定身份激活原明细页签，头部未接纳前不能跳转。</summary>
    [RelayCommand(CanExecute = nameof(CanReturnToDemand))]
    private Task ReturnToDemandAsync() => CanReturnToDemand()
        ? _navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, Header.Data!.GrossDemandId.ToString("N"), Header.Data.GrossDemandId)
        : Task.CompletedTask;
    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(Header.Data)) { OnPropertyChanged(nameof(DocumentTitle)); ReturnToDemandCommand.NotifyCanExecuteChanged(); } }
    /// <summary>会话替换或撤权时清除所有区域，已发请求不能跨会话接纳。</summary>
    private void OnUserChanged(object? sender, EventArgs e)
    {
        Header.Reset(Guid.Empty); Purchases.Reset(Guid.Empty); Receipts.Reset(Guid.Empty);
        OnPropertyChanged(nameof(CanView)); ReturnToOverviewCommand.NotifyCanExecuteChanged(); ReturnToDemandCommand.NotifyCanExecuteChanged();
    }
    private static string? Reviews(IEnumerable<string?> reasons)
    { var values = reasons.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray(); return values.Length == 0 ? null : string.Join("；", values); }
    /// <summary>页签释放时解除订阅，并取消全部物料范围读取。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _user.CurrentUserChanged -= OnUserChanged; Header.PropertyChanged -= OnHeaderChanged;
        Header.Dispose(); Purchases.Dispose(); Receipts.Dispose();
    }
}
