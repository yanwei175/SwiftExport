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
using SwiftExport.Application.Interfaces.SalesOrders.Lifecycle;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
namespace SwiftExport.Application.Services.SalesOrders.Lifecycle;

/// <summary>关闭销售订单并在同一事务处理尚未履约的来源需求。</summary>
public sealed class SalesOrderLifecycleService(ServiceBaseContext context, ISalesOrderHeadRepository headRepository, ISalesOrderLineRepository lineRepository, ISalesOrderQueryRepository queryRepository, ISourceDemandReceptionService sourceDemandReceptionService, ICurrentUser currentUser, IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : ISalesOrderLifecycleService
{
    private const string CommandType = "Orders.SalesOrder.Close";
    /// <summary>
    /// 关闭销售订单，使其退出后续编辑和确认流程。
    /// </summary>
    /// <remarks>
    /// 若订单仍有未进入下游执行的活动毛需求，会先将其作废；存在拆解或下游执行时禁止直接关闭。
    /// 关闭保留订单及明细历史，不等同于删除草稿。
    /// </remarks>
    public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request)
        => RunAsync(() => pipeline.ExecuteAsync<CloseSalesOrderRequest, SalesOrderDetailDto>(
            request, request?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteCloseCommandAsync(uid, request!, token), CancellationToken.None));

    /// <summary>原版本及规范化原因纳入指纹，路由身份先于回执校验；PG来源范围在父事务前准备。</summary>
    private async Task<ServiceResult<CommandExecutionResult<SalesOrderDetailDto>>> ExecuteCloseCommandAsync(
        Guid uid, CloseSalesOrderRequest request, CancellationToken token)
    {
        if (uid == Guid.Empty || uid != request.Uid)
            return ServiceResult<CommandExecutionResult<SalesOrderDetailDto>>.Fail(ServiceErrorCodes.Validation,
                "路由 Uid 与请求体 Uid 不一致。", "Uid");
        var result = await executor.ExecuteAsync(request.CommandId, CommandType, uid,
            new { request.Uid, request.Version, Reason = request.Reason?.Trim() },
            ct => CloseInTransactionAsync(uid, request, ct), token,
            beforeTransaction: sourceDemandReceptionService is SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document.ISourceDemandTransactionPreparation preparation
                ? ct => preparation.AcquireBeforeTransactionAsync([uid], [], ct) : null);
        return ServiceResult<CommandExecutionResult<SalesOrderDetailDto>>.Ok(result);
    }

    /// <summary>首次执行时锁头行、退出待交付来源并关闭订单，保持原事务顺序。</summary>
    private async Task<SalesOrderDetailDto> CloseInTransactionAsync(
        Guid uid, CloseSalesOrderRequest request, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            ValidateCommandIdentity(uid, request.Uid);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason)) throw new SalesOrderValidationException("关闭原因不能为空。");
            if (reason.Length > 1000) throw new SalesOrderValidationException("关闭原因不能超过 1000 个字符。");

            var header = await headRepository.GetByIdAsync(uid, true)
                ?? throw new SalesOrderValidationException("销售订单不存在或已删除。", ServiceErrorCodes.NotFound);
            if (header.DocumentStatus != SalesOrderDocumentStatusEnum.Open)
                throw new SalesOrderValidationException("销售订单已经关闭。", ServiceErrorCodes.BusinessBlocked);
            if (header.Version != request.Version)
                throw new SalesOrderValidationException("销售订单已被其他用户修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);

            var lines = await lineRepository.GetLinesByOrderIdAsync(uid, true);
            try
            {
                await sourceDemandReceptionService.ClosePendingAsync(
                    DocumentTypeStaticsEnum.SalesOrder, uid, lines.Select(line => line.Uid).ToArray(), reason);
            }
            catch (SourceDemandReceptionException ex)
            {
                throw new SalesOrderValidationException(ex.Message, ex.Code);
            }

            EnsureWriteSucceeded(await headRepository.CloseAsync(uid, request.Version, reason));
            return await queryRepository.GetDetailAsync(uid)
                ?? throw new SalesOrderValidationException("销售订单已关闭，但重新加载失败。");
    }

    /// <summary>销售生命周期权限独立于模块访问，拒绝路径不进入事务。</summary>
    private async Task<ServiceResult<SalesOrderDetailDto>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<SalesOrderDetailDto>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.SalesOrderLifecycle, token)
            ? null : ServiceResult<SalesOrderDetailDto>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有销售订单关闭权限。");
    }

    /// <summary>保留公共结果不明与命令冲突，明确映射原业务异常。</summary>
    private async Task<ServiceResult<SalesOrderDetailDto>> RunAsync(
        Func<Task<ServiceResult<SalesOrderDetailDto>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<SalesOrderDetailDto>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<SalesOrderDetailDto>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SalesOrderValidationException ex)
        { return ServiceResult<SalesOrderDetailDto>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<SalesOrderLifecycleService>()
                .LogError(ex, "销售关闭结果需按原 CommandId 核实。");
            return ServiceResult<SalesOrderDetailDto>.Fail(ServiceErrorCodes.Unexpected,
                "销售订单关闭失败，请核实命令结果。");
        }
    }

}
