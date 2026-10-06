using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using System.Text.Json;

using SwiftExport.Application.Services.SalesOrders.Internal;
using static SwiftExport.Application.Services.SalesOrders.Internal.SalesOrderRules;
using SwiftExport.Application.Interfaces.SalesOrders.Confirmation;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
namespace SwiftExport.Application.Services.SalesOrders.Confirmation;

/// <summary>以公共回执取消销售确认，来源退出及业务审计参加同一事务。</summary>
public sealed class SalesOrderCancellationService(ServiceBaseContext context, ISalesOrderHeadRepository headRepository, ISalesOrderLineRepository lineRepository, ISourceDemandReceptionService sourceDemandReceptionService, IBusinessOperationLogRepository operationLogs, ICorrelationContext correlationContext, ICurrentUser currentUser, IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : ISalesOrderCancellationService
{
    private const string Module = "SalesOrder";
    private const string CancelConfirmationOperation = "CancelConfirmation";
    private const string CommandType = "Orders.SalesOrder.CancelConfirmation";
    /// <summary>
    /// 取消销售订单确认，并按毛需求历史决定物理删除或作废保留。
    /// </summary>
    /// <remarks>
    /// 从未拆解的毛需求可以物理删除；曾拆解但已安全回退的毛需求必须作废保留历史。
    /// 一旦存在活动物料修订、物料历史缺口、PackingList 草稿或未反向正式交付，取消确认会被拒绝。
    /// <see cref="CancelSalesOrderConfirmationCommand.CommandId"/> 用作幂等键，同一命令重试返回首次执行结果。
    /// </remarks>
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
        Guid uid,
        CancelSalesOrderConfirmationCommand request)
        => RunAsync(() => pipeline.ExecuteAsync<CancelSalesOrderConfirmationCommand, SalesOrderConfirmationResultDto>(
            request, request?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteCancellationCommandAsync(uid, request!, token), CancellationToken.None));

    /// <summary>路由身份先于回执检查，原版本及Trim后原因构成指纹；PG来源范围在父事务前准备。</summary>
    private async Task<ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>> ExecuteCancellationCommandAsync(
        Guid uid, CancelSalesOrderConfirmationCommand request, CancellationToken token)
    {
        if (uid == Guid.Empty || uid != request.Uid)
            return ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>.Fail(
                ServiceErrorCodes.Validation, "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(request.CommandId, CommandType, uid,
            new { request.Uid, request.Version, Reason = request.Reason?.Trim() },
            ct => CancelInTransactionAsync(uid, request, ct), token,
            beforeTransaction: sourceDemandReceptionService is SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document.ISourceDemandTransactionPreparation preparation
                ? ct => preparation.AcquireBeforeTransactionAsync([uid], [], ct) : null);
        return ServiceResult<CommandExecutionResult<SalesOrderConfirmationResultDto>>.Ok(result);
    }

    /// <summary>首次执行时按头、行、来源顺序锁定并写入取消事实与审计。</summary>
    private async Task<SalesOrderConfirmationResultDto> CancelInTransactionAsync(
        Guid uid, CancelSalesOrderConfirmationCommand request, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            ValidateCommandIdentity(uid, request.Uid);
            var reason = request.Reason?.Trim();
            if (request.CommandId == Guid.Empty)
                throw new SalesOrderValidationException("取消确认命令标识不能为空。");
            if (string.IsNullOrWhiteSpace(reason))
                throw new SalesOrderValidationException("取消确认原因不能为空。");
            if (reason.Length > 1000)
                throw new SalesOrderValidationException("取消确认原因不能超过 1000 个字符。");

            // 第一阶段：公共命令锁之后固定订单头、订单行、毛需求的锁序。
            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new SalesOrderValidationException("销售订单不存在或已删除。", ServiceErrorCodes.NotFound);
            if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
                throw new SalesOrderValidationException("已关闭的销售订单不能取消确认。", ServiceErrorCodes.BusinessBlocked);
            if (header.ConfirmationStatus == ConfirmationStatusEnum.待确认)
                throw new SalesOrderValidationException("销售订单已经是待确认状态。", ServiceErrorCodes.BusinessBlocked);
            if (header.Version != request.Version)
                throw new SalesOrderValidationException("销售订单已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);

            // 第二阶段：锁定订单行和来源毛需求，验证一行一需求以及所有下游取消安全条件。
            var lines = await lineRepository.GetLinesByOrderIdAsync(uid, true);
            SourceDemandExitResult exitResult;
            try
            {
                exitResult = await sourceDemandReceptionService.CancelConfirmationAsync(
                    DocumentTypeStaticsEnum.SalesOrder, uid, lines.Select(line => line.Uid).ToArray(), reason);
            }
            catch (SourceDemandReceptionException ex)
            {
                throw new SalesOrderValidationException(ex.Message, ex.Code);
            }

            // 最后恢复待确认并写入业务审计；完整机器结果由公共回执保存。
            var statusResult = await headRepository.SetConfirmationStatusAsync(uid, request.Version, ConfirmationStatusEnum.待确认);
            EnsureWriteSucceeded(statusResult);
            var result = new SalesOrderConfirmationResultDto
            {
                Uid = uid,
                Version = statusResult.NewVersion ?? request.Version + 1,
                ConfirmationStatus = ConfirmationStatusEnum.待确认,
                GrossDemandCount = 0,
                HardDeletedGrossDemandCount = exitResult.HardDeletedCount,
                VoidedGrossDemandCount = exitResult.VoidedCount
            };
            await WriteCancellationLogAsync(header, request.CommandId, reason, result);
            return result;
    }

    /// <summary>
    /// 写入取消确认的业务审计，机器结果只由公共回执保存。
    /// </summary>
    private async Task WriteCancellationLogAsync(
        SalesOrderHeader header,
        Guid commandId,
        string reason,
        SalesOrderConfirmationResultDto result)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlationContext.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId,
            Module = Module,
            AggregateType = "SalesOrder",
            AggregateUid = header.Uid,
            AggregateNoSnapshot = header.OrderNo,
            OperationType = CancelConfirmationOperation,
            OperationName = "销售订单取消确认",
            Reason = reason,
            Summary = $"取消确认完成：硬删除 {result.HardDeletedGrossDemandCount} 条毛需求，作废保留 {result.VoidedGrossDemandCount} 条毛需求。",
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                BeforeVersion = header.Version,
                AfterVersion = result.Version,
                result.HardDeletedGrossDemandCount,
                result.VoidedGrossDemandCount
            }),
            UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new SalesOrderValidationException("销售订单取消确认日志写入失败。");
    }

    /// <summary>
    /// 清理日志文本并限制长度，避免超出审计字段容量。
    /// </summary>
    private static string? Normalize(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>在验证和回执读取前检查独立取消确认权限。</summary>
    private async Task<ServiceResult<SalesOrderConfirmationResultDto>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.Unauthenticated,
                "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.SalesOrderCancelConfirmation, token)
            ? null : ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.Forbidden,
                "当前账号没有销售订单取消确认权限。");
    }

    /// <summary>转换已知业务错误，保留公共执行器的冲突和结果不明语义。</summary>
    private async Task<ServiceResult<SalesOrderConfirmationResultDto>> RunAsync(
        Func<Task<ServiceResult<SalesOrderConfirmationResultDto>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SalesOrderValidationException ex)
        { return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesOrderCancellationService>()
                .LogError(ex, "销售取消确认结果需按原 CommandId 核实。");
            return ServiceResult<SalesOrderConfirmationResultDto>.Fail(ServiceErrorCodes.Unexpected,
                "销售取消确认失败，请核实命令结果。");
        }
    }

}
