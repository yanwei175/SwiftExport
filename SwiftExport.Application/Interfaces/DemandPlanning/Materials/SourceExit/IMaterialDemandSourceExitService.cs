using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;

/// <summary>定义来源维护和接收在调用方事务内所需的物料退出证明。</summary>
public interface IMaterialDemandSourceExitService
{
    /// <summary>锁定并检查指定来源的活动物料需求，不拥有事务。</summary>
    Task<bool> HasActiveAsync(Guid grossDemandId);
    /// <summary>批量锁定并评估物料历史，不代替 Shipping 交付证明或拥有事务。</summary>
    Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> AssessSourceExitAsync(IEnumerable<Guid> grossDemandIds);
}
