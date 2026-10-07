using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Sources;

/// <summary>只读取采购候选来源与物料需求，不承担预览或写入。</summary>
public sealed class PurchaseDraftCandidateService(
    ServiceBaseContext context,
    IMaterialDemandSourceRepository materialDemandFacts) : IPurchaseDraftCandidateService
{
    private readonly ILogger<PurchaseDraftCandidateService> _logger = context.LoggerFactory.CreateLogger<PurchaseDraftCandidateService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await materialDemandFacts.GetPurchaseCandidateSourceGroupsAsync(cancellationToken);
            return ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询采购候选来源失败。");
            return ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Fail("查询采购候选来源失败。");
        }
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default)
    {
        request ??= new PurchaseDraftCandidateQuery();
        request.SourceType = request.SourceType?.Trim();
        if (request.SourceUid.HasValue != !string.IsNullOrWhiteSpace(request.SourceType) || request.SourceUid == Guid.Empty)
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Fail(PurchaseDraftErrorCodes.InvalidSource, "来源类型和来源 Uid 必须同时提供或同时为空。");

        try
        {
            var rows = await materialDemandFacts.GetPurchaseCandidatesAsync(request, cancellationToken);
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([.. rows]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询待采购物料失败。");
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Fail("查询待采购物料失败。");
        }
    }
}
