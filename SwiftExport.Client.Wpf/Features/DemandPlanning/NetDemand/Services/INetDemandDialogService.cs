using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public interface INetDemandDialogService
{
    /// <summary>打开来源需求选择器，并返回本次对话框是否已成功建立过物料需求。</summary>
    Task<bool> ShowSourceDemandCandidatesAsync(
        CancellationToken cancellationToken = default);
    Task<bool> ShowPurchaseDraftPreviewAsync(
        PurchaseDraftPreviewOpenRequest request,
        CancellationToken cancellationToken = default);
}
