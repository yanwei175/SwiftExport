using System.Data;
using System.Text.Json;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.MasterData;

namespace SwiftExport.Application.Services.MasterData;

/// <summary>最小主数据启停命令；资格变更与操作日志原子提交，不改变任何历史订单。</summary>
public sealed class ExecutionEligibilityService(
    ServiceBaseContext context,
    IExecutionEligibilityRepository repository,
    IBusinessOperationLogRepository operationLogs,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext) : IExecutionEligibilityService
{
    /// <summary>重验管理员和主数据版本后，将启停状态与带原因的业务日志原子提交。</summary>
    public async Task<ServiceResult<ExecutionEligibilityStatusResult>> SetStatusAsync(
        SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail("FORBIDDEN：需要真实有效的管理员身份。");
        if (command == null || !Enum.IsDefined(typeof(ExecutionEligibilityKind), command.Kind)
            || command.Uid == Guid.Empty || command.ExpectedVersion <= 0 || command.ExpectedVersion == int.MaxValue
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail("INVALID_COMMAND：对象、版本和不超过500字的变更原因必填。");
        if (context.Session.Transaction != null)
            throw new InvalidOperationException("执行资格命令必须独立拥有业务事务。");

        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            if (!await repository.IsActiveAdministratorAsync(userId, cancellationToken))
                return FailAndRollback("FORBIDDEN：账号或管理员授权已失效。");
            var previous = await repository.GetForUpdateAsync(command.Kind, command.Uid, cancellationToken);
            if (previous == null)
                return FailAndRollback("NOT_FOUND：主数据不存在或已删除。");
            if (previous.Version != command.ExpectedVersion)
                return FailAndRollback("CONCURRENCY_CONFLICT：资料版本已变化，请刷新后重试。");
            if (previous.IsActive == command.IsActive)
                return FailAndRollback("STATE_UNCHANGED：目标资格与当前一致，无需重复变更。");
            if (await repository.SetStatusAsync(command, cancellationToken) != 1)
                return FailAndRollback("CONCURRENCY_CONFLICT：资格更新失败，请刷新后重试。");

            var log = new BusinessOperationLog
            {
                CommandId = Guid.NewGuid(),
                CorrelationId = correlationContext.CorrelationId,
                Module = "MasterData",
                AggregateType = command.Kind.ToString(),
                AggregateUid = command.Uid,
                OperationType = "SetExecutionEligibility",
                OperationName = command.IsActive ? "恢复新业务资格" : "停用新业务资格",
                Reason = command.Reason.Trim(),
                Summary = "只变更新业务引用资格，不影响既有业务承诺。",
                ChangedFieldsJson = JsonSerializer.Serialize(new { IsActive = new { Before = previous.IsActive, After = command.IsActive } }),
                UserUid = userId,
                UserNameSnapshot = currentUser.UserName,
                OccurredDate = DateTime.Now
            };
            if (await operationLogs.InsertAsync(log) != 1)
                throw new InvalidOperationException("执行资格变更日志写入失败。");
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return ServiceResult<ExecutionEligibilityStatusResult>.Ok(new ExecutionEligibilityStatusResult
            {
                Kind = command.Kind, Uid = command.Uid, Version = command.ExpectedVersion + 1, IsActive = command.IsActive
            });
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            context.Session.Transaction = null;
        }

        // 业务拒绝也必须结束事务，防止角色和主数据锁泄漏到同一请求的后续操作。
        ServiceResult<ExecutionEligibilityStatusResult> FailAndRollback(string message)
        {
            transaction.Rollback();
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail(message);
        }
    }
}
