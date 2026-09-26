using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Sources;

/// <summary>提供待建立来源单分组及候选行只读查询。</summary>
public sealed class MaterialDemandSourceQueryService(ServiceBaseContext context, IMaterialDemandSourceRepository netDemandRepository) : IMaterialDemandSourceQueryService
{
    private readonly ILogger<MaterialDemandSourceQueryService> _logger = context.LoggerFactory.CreateLogger<MaterialDemandSourceQueryService>();

    /// <summary>查询待建立来源单分组，分组资格与物料领域候选规则保持一致。</summary>
    public async Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await netDemandRepository.GetSourceDemandCandidateGroupsAsync(cancellationToken);
            return ServiceResult<List<SourceDemandCandidateGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询待建立来源单分组失败。");
            return ServiceResult<List<SourceDemandCandidateGroupDto>>.Fail("查询待建立来源单失败。");
        }
    }

    /// <summary>按来源类型和来源 Uid 完整查询一张来源单的待建立行。</summary>
    public async Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceUid == Guid.Empty)
            return ServiceResult<List<SourceDemandCandidateDto>>.Fail("来源类型和来源 Uid 不能为空。");
        try
        {
            var items = await netDemandRepository.GetSourceDemandCandidatesBySourceAsync(
                sourceType.Trim(), sourceUid, cancellationToken);
            return ServiceResult<List<SourceDemandCandidateDto>>.Ok([.. items]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源 {SourceType}/{SourceUid} 的待建立行失败。", sourceType, sourceUid);
            return ServiceResult<List<SourceDemandCandidateDto>>.Fail("查询来源单待建立明细失败。");
        }
    }

}
