using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Core.Interfaces.MasterData;

/// <summary>在调用方事务中读取有效执行资格授权及主数据状态，并按版本更新资格。</summary>
public interface IExecutionEligibilityRepository
{
    /// <summary>业务事务开始后、任何查询或写入之前准备当前对象范围；不拥有事务，源SQL路径可无操作，PG须防止目标等待建立旧快照。</summary>
    Task PrepareTargetBeforeSnapshotAsync(ExecutionEligibilityKind kind, CancellationToken cancellationToken = default);

    /// <summary>锁定并验证活动用户的系统管理员角色或执行资格权限，不使用客户端角色信息。</summary>
    Task<bool> HasExecutionEligibilityPermissionAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>锁定未软删除主数据的当前资格；未知对象返回空。</summary>
    Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(
        ExecutionEligibilityKind kind, Guid uid, CancellationToken cancellationToken = default);

    /// <summary>按预期版本修改资格并打审计戳；零行表示并发冲突。</summary>
    Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default);
}
