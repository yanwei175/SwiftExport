using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>定义物料领域从来源需求建立、回退及校验活动物料事实的权威用例。</summary>
public interface IMaterialDemandEstablishmentService
{
    Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>在调用方事务内锁定并判断来源需求是否存在活动物料需求。</summary>
    Task<bool> HasActiveAsync(Guid grossDemandId);
    /// <summary>协调来源取消或关闭前的物料、执行及正式履约阻断检查。</summary>
    Task<bool> HasSourceExitBlockerAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds);
    /// <summary>取得来源退出所需的物料历史行数。</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetHistoryCountsForUpdateAsync(IEnumerable<Guid> grossDemandIds);
    /// <summary>验证来源退出前的物料历史已完整安全回退。</summary>
    Task<bool> HasInvalidExitHistoryAsync(IEnumerable<Guid> grossDemandIds);
}
