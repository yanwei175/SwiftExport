using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>协调采购预览和供应商查询；采购单头由后端预览统一组装。</summary>
public sealed class PurchasePreparationService(
    IPurchaseDraftApi purchaseDraftApi,
    IProcurementMasterDataQueryApi masterDataApi,
    IApiCallRunner apiCallRunner)
{
    /// <summary>查询可用供应商；页面自行决定 latest-request-wins 与展示状态。</summary>
    public Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(
        PagedRequestBase request,
        CancellationToken cancellationToken) => apiCallRunner.RunAsync(
            () => masterDataApi.SearchSuppliersAsync(request, cancellationToken),
            cancellationToken,
            "搜索供应商失败");

    /// <summary>生成包含可信只读单头的权威采购预览。</summary>
    public async Task<ServiceResult<PurchasePreparationResult>> PrepareAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var previewResult = await apiCallRunner.RunAsync(
            () => purchaseDraftApi.PreviewAsync(request, cancellationToken),
            cancellationToken,
            "生成采购预览失败");
        if (!previewResult.Success || previewResult.Data is null)
            return string.IsNullOrWhiteSpace(previewResult.Error?.Code)
                ? ServiceResult<PurchasePreparationResult>.Fail(previewResult.Message)
                : ServiceResult<PurchasePreparationResult>.Fail(
                    previewResult.Error.Code,
                    previewResult.Error.Message ?? previewResult.Message,
                    previewResult.Error.Field);

        return ServiceResult<PurchasePreparationResult>.Ok(new PurchasePreparationResult
        {
            Preview = previewResult.Data,
        });
    }
}

/// <summary>采购准备结果，仅承载后端已经组装完成的权威预览。</summary>
public sealed class PurchasePreparationResult
{
    public required PurchaseDraftPreviewResponse Preview { get; init; }
}
