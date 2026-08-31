using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.MasterData;

/// <summary>负责主数据执行资格的权限、并发和变更日志事务。</summary>
public interface IExecutionEligibilityService
{
    /// <summary>变更新业务资格，不回写或取消已有承诺。</summary>
    Task<ServiceResult<ExecutionEligibilityStatusResult>> SetStatusAsync(
        SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default);
}
