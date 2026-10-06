using SwiftExport.Contracts.DTOs.Errors;
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
using SwiftExport.Core.Interfaces.Commands;
using Microsoft.Extensions.Logging;

namespace SwiftExport.Application.Services.MasterData;

/// <summary>最小主数据启停命令；资格变更与操作日志原子提交，不改变任何历史订单。</summary>
public sealed class ExecutionEligibilityService(
    ServiceBaseContext context,
    IExecutionEligibilityRepository repository,
    IBusinessOperationLogRepository operationLogs,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext) : IExecutionEligibilityService
{
    /// <summary>先准备同一审计命令租约，再在原可串行化事务内重验授权及版本，将资格与日志原子提交。</summary>
    public async Task<ServiceResult<ExecutionEligibilityStatusResult>> SetStatusAsync(
        SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail(ServiceErrorCodes.Unauthenticated, "需要真实有效的登录身份。");
        if (command == null || !Enum.IsDefined(typeof(ExecutionEligibilityKind), command.Kind)
            || command.Uid == Guid.Empty || command.ExpectedVersion <= 0 || command.ExpectedVersion == int.MaxValue
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail(ServiceErrorCodes.Validation, "对象、版本和不超过500字的变更原因必填。");
        if (context.Session.Transaction != null)
            throw new InvalidOperationException("执行资格命令必须独立拥有业务事务。");

        var commandId = Guid.NewGuid();
        IAsyncDisposable? lease = null;
        IDbTransaction? transaction = null;
        try
        {
            // PG 高隔离审计须在快照建立前取得同一命令租约；没有可选能力的原日志仓储沿用直接事务路径。
            if (operationLogs is ICommandTransactionPreparation preparation)
            {
                lease = await preparation.AcquireBeforeTransactionAsync(commandId, cancellationToken);
                if (lease is null) throw new TimeoutException("执行资格审计命令准备超时。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
            context.Session.Transaction = transaction;
            // PG须在任何快照读取前排队当前对象的写入/行锁持有者；授权判断仍在目标读取和版本判断之前。
            await repository.PrepareTargetBeforeSnapshotAsync(command.Kind, cancellationToken);
            if (!await repository.HasExecutionEligibilityPermissionAsync(userId, cancellationToken))
                return FailAndRollback(ServiceErrorCodes.Forbidden, "账号或执行资格授权已失效。");
            var previous = await repository.GetForUpdateAsync(command.Kind, command.Uid, cancellationToken);
            if (previous == null)
                return FailAndRollback(ServiceErrorCodes.NotFound, "主数据不存在或已删除。");
            if (previous.Version != command.ExpectedVersion)
                return FailAndRollback(ServiceErrorCodes.VersionConflict, "资料版本已变化，请刷新后重试。");
            if (previous.IsActive == command.IsActive)
                return FailAndRollback(ServiceErrorCodes.StateBlocked, "目标资格与当前一致，无需重复变更。");
            if (await repository.SetStatusAsync(command, cancellationToken) != 1)
                return FailAndRollback(ServiceErrorCodes.VersionConflict, "资格更新失败，请刷新后重试。");

            var log = new BusinessOperationLog
            {
                CommandId = commandId,
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
            if (transaction is not null) RollbackSafely(transaction);
            throw;
        }
        finally
        {
            // 事务先彻底释放再清空引用，最后释放准备租约；清理异常不能遮蔽已提交结果、业务拒绝或原始异常。
            try
            {
                try { transaction?.Dispose(); }
                catch (Exception exception)
                {
                    TryLogCleanup(exception, "执行资格事务释放失败，关闭连接。");
                    try { context.Session.Connection.Dispose(); }
                    catch (Exception closeException) { TryLogCleanup(closeException, "执行资格连接清理失败。"); }
                }
            }
            finally
            {
                if (transaction is not null) context.Session.Transaction = null;
                if (lease is not null)
                {
                    try { await lease.DisposeAsync(); }
                    catch (Exception exception) { TryLogCleanup(exception, "执行资格审计租约释放失败。"); }
                }
            }
        }

        // 业务拒绝也必须结束事务，防止角色和主数据锁泄漏到同一请求的后续操作。
        ServiceResult<ExecutionEligibilityStatusResult> FailAndRollback(string code, string message)
        {
            if (transaction is not null) RollbackSafely(transaction);
            return ServiceResult<ExecutionEligibilityStatusResult>.Fail(code, message);
        }
    }

    /// <summary>回滚失败由事务释放兜底，保留原业务拒绝、数据库异常或取消，不改变服务错误映射。</summary>
    private void RollbackSafely(IDbTransaction transaction)
    {
        try { transaction.Rollback(); }
        catch (Exception exception) { TryLogCleanup(exception, "执行资格事务回滚失败，将继续释放事务。"); }
    }

    /// <summary>资源清理诊断失败不阻断其他清理，也不能替换调用方已确定的结果。</summary>
    private void TryLogCleanup(Exception exception, string message)
    {
        try { context.LoggerFactory.CreateLogger<ExecutionEligibilityService>().LogError(exception, "{CleanupMessage}", message); }
        catch { /* 日志提供者失败仍继续清理本层事务及租约。 */ }
    }
}
