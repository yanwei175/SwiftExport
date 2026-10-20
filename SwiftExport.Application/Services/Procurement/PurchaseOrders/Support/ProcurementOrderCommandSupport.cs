using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;

/// <summary>采购订单写命令共用的细权限、错误转换、结果与业务审计边界。</summary>
public sealed class ProcurementOrderCommandSupport(
    IPermissionEvaluationService permissions, ICurrentUser user, IBusinessOperationLogRepository logs,
    ICorrelationContext correlation, ILogger<ProcurementOrderCommandSupport> logger)
{
    /// <summary>在参数验证和回执读取前核对当前采购细权限，重放同样需要授权。</summary>
    public async Task<ServiceResult<T>?> AuthorizeAsync<T>(string operation, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "未认证，不能访问采购。");
        var key = operation switch
        {
            "Create" => PermissionKeys.ProcurementOrderCreate,
            "Edit" => PermissionKeys.ProcurementOrderEdit,
            "ProgressReport" => PermissionKeys.ProcurementProgressReport,
            "Confirm" => PermissionKeys.ProcurementOrderConfirm,
            "Void" => PermissionKeys.ProcurementOrderVoid,
            _ => string.Empty
        };
        if (!await permissions.HasPermissionAsync(actor, key, ct))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "没有当前采购操作权限。");
        return null;
    }

    /// <summary>把事务内业务拒绝和公共执行器结果转换为既有服务结果；未知提交状态保持可重试语义。</summary>
    public async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try { ct.ThrowIfCancellationRequested(); return ServiceResult<T>.Ok(await action()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (BusinessCommandException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (ProcurementOperationException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (InvalidOperationException ex)
        { return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, ex.Message); }
        catch (Exception ex)
        {
            logger.LogError(ex, "采购命令结果未能确认。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.OutcomeUnknown, "采购操作结果未能确认；请保留同一命令标识重试。");
        }
    }

    /// <summary>按销售订单的仓储结果协议拒绝失败写入，必须在事务内检查，防止部分结果提交。</summary>
    public static void EnsureWriteSucceeded(RepositoryWriteResult result)
    {
        if (result.Success) return;
        var code = result.ErrorCode switch
        {
            ServiceErrorCodes.ConcurrencyConflict => ServiceErrorCodes.VersionConflict,
            ServiceErrorCodes.VersionConflict or ServiceErrorCodes.NotFound or ServiceErrorCodes.Validation
                or ServiceErrorCodes.BusinessBlocked => result.ErrorCode,
            _ => ServiceErrorCodes.BusinessBlocked
        };
        throw new ProcurementOperationException(
            code,
            result.Message ?? "数据库写入失败。",
            code == ServiceErrorCodes.VersionConflict ? "ExpectedVersion" : null);
    }

    /// <summary>检查锁定读取的错误后才允许消费事实；冲突沿用版本错误，不使用未完整锁定的数据。</summary>
    public static SwiftExport.Core.Models.Procurement.ProcurementReversalFacts RequireLockedFacts(
        (SwiftExport.Core.Models.Procurement.ProcurementReversalFacts? Facts, ServiceErrorDto? Error) result)
    {
        if (result.Error is { } error)
            throw new ProcurementOperationException(
                error.Code == ServiceErrorCodes.ConcurrencyConflict ? ServiceErrorCodes.VersionConflict : error.Code,
                error.Message, error.Code is ServiceErrorCodes.ConcurrencyConflict or ServiceErrorCodes.VersionConflict ? "ExpectedVersion" : error.Field);
        return result.Facts ?? throw new InvalidOperationException("采购锁定读取未返回完整事实。");
    }

    /// <summary>将订单头行当前版本压缩为持久化成功回执的结果。</summary>
    public static ProcurementOrderCommandResult Result(PurchaseOrderHead head, List<PurchaseOrderLine> lines) => new()
    {
        PurchaseOrderId = head.Uid, OrderNo = head.OrderNo, Version = head.Version,
        ConfirmationStatus = head.ConfirmationStatus, LifecycleStatus = head.LifecycleStatus, ConfirmationId = head.ActiveConfirmationId,
        Lines = lines.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList()
    };

    /// <summary>首次执行时同事务保存采购业务日志；回执重放跳过本方法。</summary>
    public async Task RecordSuccessAsync(Guid commandId, string operation, ProcurementOrderCommandResult result, CancellationToken ct)
    {
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            CorrelationId = correlation.CorrelationId, Module = "Procurement.OrderManagement",
            AggregateType = "PurchaseOrder", AggregateUid = result.PurchaseOrderId, AggregateNoSnapshot = result.OrderNo,
            OperationType = operation, OperationName = "独立采购" + operation,
            Summary = "独立采购命令成功；详情见原始回执及确认依据。",
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now,
            ChangedFieldsJson = JsonSerializer.Serialize(result)
        }) != 1) throw new InvalidOperationException("采购业务日志写入失败。");
    }

    /// <summary>每来源命令仅追加一条汇总审计，完整列出新建和追加PO；命令回执仍负责原结果重放，Trace按结构化关联定位各PO。</summary>
    public async Task RecordDemandDraftsAsync(Guid sourceCommandId, Guid executionRequestId, CreatePurchaseDraftSourceResult result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var drafts=result.PurchaseDrafts.OrderBy(draft=>draft.PurchaseOrderId).ToArray();
        if(await logs.InsertAsync(new BusinessOperationLog
        {
            Uid=GuidSequentialGenerator.NewGuid(),CommandId=sourceCommandId,CorrelationId=correlation.CorrelationId,
            Module="Procurement.OrderManagement",AggregateType="PurchaseDraftSource",AggregateUid=result.SourceUid,
            OperationType="CreateDraftsFromDemand",OperationName="由需求创建或追加采购草稿",
            Summary=$"本来源新建 {drafts.Count(draft=>!draft.AppendedToExisting)} 份，追加 {drafts.Count(draft=>draft.AppendedToExisting)} 份采购草稿：{string.Join("、",drafts.Select(draft=>draft.OrderNo))}。",
            ChangedFieldsJson=JsonSerializer.Serialize(new{ExecutionRequestId=executionRequestId,SourceCommandId=sourceCommandId,result.SourceType,result.SourceUid,PurchaseDrafts=drafts}),
            RelatedObjectsJson=JsonSerializer.Serialize(drafts),UserUid=user.UserId,UserNameSnapshot=user.UserName,OccurredDate=DateTime.Now
        })!=1)throw new InvalidOperationException("需求采购来源汇总审计写入失败。");
    }
}
