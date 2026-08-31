using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Core.Interfaces.MasterData;

/// <summary>在调用方事务中读取真实管理员身份及主数据状态，并按版本更新资格。</summary>
public interface IExecutionEligibilityRepository
{
    /// <summary>锁定并验证数据库中的活动用户、角色关联和管理员角色，不使用客户端角色信息。</summary>
    Task<bool> IsActiveAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>锁定未软删除主数据的当前资格；未知对象返回空。</summary>
    Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(
        ExecutionEligibilityKind kind, Guid uid, CancellationToken cancellationToken = default);

    /// <summary>按预期版本修改资格并打审计戳；零行表示并发冲突。</summary>
    Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default);
}
