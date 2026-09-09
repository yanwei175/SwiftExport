using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public interface INetDemandDialogService
{
    /// <summary>打开来源需求候选列表并返回用户勾选的纯业务 DTO。</summary>
    Task<IReadOnlyList<SourceDemandCandidateDto>> ShowSourceDemandCandidatesAsync(
        CancellationToken cancellationToken = default);
    Task<bool> ShowPurchaseDraftPreviewAsync(
        PurchaseDraftPreviewOpenRequest request,
        CancellationToken cancellationToken = default);
}
