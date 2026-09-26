using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;

/// <summary>采购只读权限与一致性读取边界。</summary>
public sealed class ProcurementOrderReadSupport(
    ServiceBaseContext context,
    IPermissionEvaluationService permissions,
    ICurrentUser user)
{
    /// <summary>读取订单、管理页或供应商跟进投影；普通查询不占用业务事务。</summary>
    public Task<ServiceResult<T>> ReadAsync<T>(string operation, Func<Task<T>> action, CancellationToken ct)
        => ExecuteReadAsync(operation, action, ct, false);

    /// <summary>在 Serializable 事务中读取确认依据或反向预览，确保读取的业务事实一致。</summary>
    public Task<ServiceResult<T>> ReadConsistentAsync<T>(string operation, Func<Task<T>> action, CancellationToken ct)
        => ExecuteReadAsync(operation, action, ct, true);

    /// <summary>统一只读权限、异常转换和可选的一致性事务；不执行写命令或保存回执。</summary>
    private async Task<ServiceResult<T>> ExecuteReadAsync<T>(string operation, Func<Task<T>> action, CancellationToken ct, bool consistent)
    {
        if (!user.IsAuthenticated || !user.UserId.HasValue)
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unauthenticated, "未认证，不能访问采购。");
        if (context.Session.Transaction is not null)
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, "请求已存在事务。");
        IDbTransaction? transaction = null;
        try
        {
            if (!await permissions.HasPermissionAsync(user.UserId.Value, ResolvePermissionKey(operation), ct))
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Forbidden, "没有当前采购操作权限。");
            if (consistent)
            {
                transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
                context.Session.Transaction = transaction;
            }
            var result = await action();
            ct.ThrowIfCancellationRequested();
            transaction?.Commit();
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
            context.LoggerFactory.CreateLogger<ProcurementOrderReadSupport>().LogError(ex, "采购读取 {Operation} 失败。", operation);
            return ServiceResult<T>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "采购操作失败；写命令若未收到响应，请保留同一命令标识重试。");
        }
        finally { context.Session.Transaction = null; transaction?.Dispose(); }

        void RollbackSafely()
        {
            try { if (transaction?.Connection is not null) transaction.Rollback(); }
            catch (Exception ex)
            {
                context.LoggerFactory.CreateLogger<ProcurementOrderReadSupport>().LogWarning(ex, "采购读取事务回滚清理失败。");
            }
        }
    }

    /// <summary>把既有采购操作名映射为稳定细权限键；未知操作保持拒绝。</summary>
    private static string ResolvePermissionKey(string operation) => operation switch
    {
        "Read" => PermissionKeys.ProcurementOrderRead,
        "Edit" => PermissionKeys.ProcurementOrderEdit,
        "Confirm" => PermissionKeys.ProcurementOrderConfirm,
        "Void" => PermissionKeys.ProcurementOrderVoid,
        _ => string.Empty
    };
}
