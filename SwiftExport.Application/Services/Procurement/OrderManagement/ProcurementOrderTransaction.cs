using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>新采购用例的事务与服务端权限边界；授权先于命令重放，失败不提交部分事实。</summary>
public sealed class ProcurementOrderTransaction(ServiceBaseContext context, IProcurementOrderRepository repository, ICurrentUser user)
{
    /// <summary>在独立可串行化事务中执行一个用例，保持资格、BOM 和写入一致。</summary>
    public async Task<ServiceResult<T>> ExecuteAsync<T>(string operation, Func<Task<T>> action, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !user.UserId.HasValue)
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unauthenticated, "未认证，不能访问采购。");
        if (context.Session.Transaction is not null)
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, "请求已存在事务。");
        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            if (!await repository.HasPermissionAsync(user.UserId.Value, operation, ct))
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Forbidden, "没有当前采购操作权限。");
            var result = await action();
            ct.ThrowIfCancellationRequested();
            transaction.Commit();
            return ServiceResult<T>.Ok(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { RollbackSafely(); throw; }
        catch (ProcurementOperationException ex)
        {
            RollbackSafely();
            return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field);
        }
        catch (ProcurementConcurrencyException ex)
        {
            RollbackSafely();
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, ex.Message, "ExpectedVersion");
        }
        catch (InvalidOperationException ex)
        {
            RollbackSafely();
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, ex.Message);
        }
        catch (Exception ex)
        {
            RollbackSafely();
            context.LoggerFactory.CreateLogger<ProcurementOrderTransaction>().LogError(ex, "独立采购操作 {Operation} 失败。", operation);
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "采购操作失败；写命令若未收到响应，请保留同一命令标识重试。");
        }
        finally { context.Session.Transaction = null; }

        // 死锁或触发器可能已由服务器回滚；回滚清理不能遮蔽最初的业务拒绝或取消。
        void RollbackSafely()
        {
            try { if (transaction.Connection is not null) transaction.Rollback(); }
            catch (Exception ex)
            {
                context.LoggerFactory.CreateLogger<ProcurementOrderTransaction>().LogWarning(ex, "采购事务回滚清理失败，将释放事务连接。");
            }
        }
    }
}
