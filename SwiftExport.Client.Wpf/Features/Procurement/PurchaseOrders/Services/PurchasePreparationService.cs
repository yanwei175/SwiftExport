using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
/// <summary>采购准备页的供应商候选查询；准备阶段不发送预览或写入请求。</summary>
public sealed class PurchasePreparationService
{
    private readonly IProcurementMasterDataQueryApi _masterData;
    private readonly IApiCallRunner _runner;
    /// <summary>注入候选与统一 API 边界。</summary>
    public PurchasePreparationService(IPurchaseDraftApi purchaseDraftApi, IProcurementMasterDataQueryApi masterDataApi, IApiCallRunner apiCallRunner)
    { _masterData = masterDataApi; _runner = apiCallRunner; }
    /// <summary>页面负责当前会话和最新请求接纳，本服务仅执行候选查询。</summary>
    public Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken)
        => _runner.RunAsync(() => _masterData.SearchSuppliersAsync(request, cancellationToken), cancellationToken, "搜索供应商失败");
}