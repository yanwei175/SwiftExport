using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;

/// <summary>定义尚可建立物料需求的来源分组及候选行查询。</summary>
public interface IMaterialDemandSourceQueryService
{
    /// <summary>查询包含待建立来源行的来源单分组。</summary>
    Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>按来源身份查询完整候选行。</summary>
    Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default);
}
