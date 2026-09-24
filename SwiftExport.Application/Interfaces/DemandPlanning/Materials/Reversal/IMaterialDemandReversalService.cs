using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;

/// <summary>定义整批物料需求修订的安全回退用例。</summary>
public interface IMaterialDemandReversalService
{
    /// <summary>按来源稳定顺序锁定，阻断活动下游后整批回退。</summary>
    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(RollbackMaterialDemandsCommand command, CancellationToken cancellationToken = default);
}
