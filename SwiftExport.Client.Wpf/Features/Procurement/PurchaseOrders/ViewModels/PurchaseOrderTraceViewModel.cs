using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>按采购单 Uid 显示需求来源、供应商反馈及正式入库的只读证据链。</summary>
public sealed partial class PurchaseOrderTraceViewModel(
    IPurchaseOrderTraceApi api, IApiCallRunner runner, INavigationService navigation,
    ICurrentUserService currentUser) : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-order-trace";
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    public partial Guid PurchaseOrderId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyPropertyChangedFor(nameof(OriginText))]
    public partial PurchaseOrderTraceDto? Detail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenMaterialTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSourceTraceCommand))]
    public partial PurchaseOrderTraceLinkDto? SelectedLink { get; set; }

    [ObservableProperty]
    public partial PurchaseOrderTraceLineDto? SelectedLine { get; set; }

    partial void OnSelectedLineChanged(PurchaseOrderTraceLineDto? value)
    {
        SelectedLink = value?.Links.FirstOrDefault();
        SelectedReceipt = value?.Receipts.FirstOrDefault();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInboundCommand))]
    public partial PurchaseOrderTraceReceiptDto? SelectedReceipt { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

    /// <summary>页签标题包含采购单号，便于跨单据查证。</summary>
    public string DocumentTitle => Detail is null ? "采购追溯" : $"采购追溯 {Detail.OrderNo}";
    /// <summary>保留采购创建类别，按实际 Link 展示后续关联及释放历史，未知类别不由关联集合推断。</summary>
    public string OriginText => Detail?.OriginKind switch
    {
        2 when Detail.Lines.Any(line => line.Links.Count > 0) => "独立采购；各行需求关联及历史以 Link 为准。",
        2 => "独立采购，无需求关联。",
        1 => "需求驱动采购；各行来源以 Link 为准。",
        _ => "来源类别未确认，请核实历史采购单。"
    };
    public bool HasUnsavedChanges => false;
    /// <summary>只读页签没有待保存修改，可直接关闭。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <summary>以导航参数初始化文档，刷新仅重新查询服务端事实。</summary>
    public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsReadOnly = true;
        PurchaseOrderId = parameter is PurchaseOrderTraceOpenRequest request ? request.PurchaseOrderId : Guid.Empty;
        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(CancellationToken.None);

    /// <summary>取消旧查询并仅接纳仍属于当前采购单和存活页签的响应。</summary>
    private async Task LoadAsync(CancellationToken navigationToken)
    {
        _loadCancellation?.Cancel();
        if (_disposed || PurchaseOrderId == Guid.Empty)
        {
            StatusText = "缺少采购单标识。";
            return;
        }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(navigationToken);
        _loadCancellation = cancellation;
        var purchaseId = PurchaseOrderId;
        try
        {
            await RunBusyAsync(async () =>
            {
                var result = await runner.RunAsync(() => api.GetAsync(purchaseId, cancellation.Token),
                    cancellation.Token, "加载采购追溯失败");
                if (_disposed || cancellation.IsCancellationRequested
                    || !ReferenceEquals(_loadCancellation, cancellation) || PurchaseOrderId != purchaseId) return;
                if (!result.Success || result.Data is null)
                {
                    // 读取未通过历史门禁时移除旧数量，只保留服务端核实提示。
                    Detail = null;
                    SelectedLine = null;
                    SelectedLink = null;
                    SelectedReceipt = null;
                    StatusText = result.Message ?? "采购追溯读取失败。";
                    return;
                }
                Detail = result.Data;
                SelectedLine = Detail.Lines.FirstOrDefault();
                SelectedLink = SelectedLine?.Links.FirstOrDefault();
                SelectedReceipt = SelectedLine?.Receipts.FirstOrDefault();
                StatusText = $"共 {Detail.Lines.Count} 条采购行；已删除行和反向记录仍保留供核查。";
            }, "正在加载采购追溯...", skipIfBusy: false);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>由需求 Link 的稳定物料 Uid 打开物料追踪。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenMaterialTrace))]
    private Task OpenMaterialTraceAsync() => navigation.OpenDocumentAsync(
        MaterialDemandTraceViewModel.RouteKey, $"material-demand-trace:{SelectedLink!.NetDemandId:N}",
        new MaterialDemandTraceOpenRequest { NetDemandId = SelectedLink.NetDemandId });

    private bool CanOpenMaterialTrace() => SelectedLink is { NetDemandId: var id } && id != Guid.Empty
        && currentUser.HasPermission(PermissionKeys.DemandPlanningMaterialView);

    /// <summary>在来源身份证据完整且有权限时跳转成品需求追踪。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSourceTrace))]
    private Task OpenSourceTraceAsync() => navigation.OpenDocumentAsync(
        GrossDemandTraceViewModel.RouteKey, $"gross-demand-trace:{SelectedLink!.GrossDemandId!.Value:N}",
        new GrossDemandTraceOpenRequest(SelectedLink.GrossDemandId.Value));

    private bool CanOpenSourceTrace() => SelectedLink?.GrossDemandId is { } id && id != Guid.Empty
        && currentUser.HasPermission(PermissionKeys.DemandPlanningSourceView);

    /// <summary>按正式入库单号打开入库管理文档并定位原单。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenInbound))]
    private Task OpenInboundAsync() => navigation.OpenDocumentAsync(
        SwiftExportClient.Features.Inventory.Inbounds.ViewModels.InboundManagerViewModel.RouteKey,
        $"inbound-document:{SelectedReceipt!.InboundDocumentId:N}",
        new SwiftExportClient.Features.Inventory.Inbounds.Models.InboundDocumentOpenRequest
        {
            InboundDocumentId = SelectedReceipt.InboundDocumentId,
            DocumentNo = SelectedReceipt.DocumentNo,
            PostingStatus = SelectedReceipt.PostingStatus
        });

    private bool CanOpenInbound() => SelectedReceipt is { InboundDocumentId: var id } && id != Guid.Empty
        && currentUser.HasPermission(PermissionKeys.InventoryInboundView);

    /// <summary>关闭页签时取消在途追溯请求，防止旧响应写入释放后的页面。</summary>
    public void Dispose()
    {
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
