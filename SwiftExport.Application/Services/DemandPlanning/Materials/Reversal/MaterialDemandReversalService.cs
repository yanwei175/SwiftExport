using SwiftExport.Contracts.DTOs.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Application.Services.DemandPlanning.Materials;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Reversal;

/// <summary>整批安全回退来源物料修订；公共命令组件负责授权边界、事务及回执。</summary>
public sealed class MaterialDemandReversalService(
    ServiceBaseContext context,
    IGrossDemandDocumentRepository grossDemandRepository,
    IMaterialDemandDocumentRepository netDemandRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser,
    IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : IMaterialDemandReversalService
{
    private const string Module = "MaterialDemand";
    private const string RollbackOperation = "RollbackSourceEstablishment";
    private const string CommandType = PermissionKeys.DemandPlanningMaterialRollback;

    /// <summary>回退选定来源的活动物料修订；活动执行关系存在时整批命令回滚。</summary>
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => RunCommandAsync(() => pipeline.ExecuteAsync<RollbackMaterialDemandsCommand, RollbackMaterialDemandsResult>(
            command, command?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteRollbackCommandAsync(command!, token), cancellationToken));

    /// <summary>以原原因和稳定修订集合读取回执；PG来源身份先于父事务发现及准备，不提前执行回退校验。</summary>
    private async Task<ServiceResult<CommandExecutionResult<RollbackMaterialDemandsResult>>> ExecuteRollbackCommandAsync(
        RollbackMaterialDemandsCommand command, CancellationToken token)
    {
        var input = command.Items.OrderBy(item => item.GrossDemandId)
            .Select(item => new { item.GrossDemandId, item.DecompositionRevision }).ToArray();
        var result = await executor.ExecuteAsync(command.CommandId, CommandType, null,
            new { Items = input, Reason = command.Reason.Trim() },
            ct => ReverseInTransactionAsync(command, ct), token,
            beforeTransaction: grossDemandRepository is ISourceDemandTransactionPreparation preparation
                ? ct => preparation.AcquireBeforeTransactionAsync([], input.Select(x => x.GrossDemandId).ToArray(), ct) : null);
        return ServiceResult<CommandExecutionResult<RollbackMaterialDemandsResult>>.Ok(result);
    }

    /// <summary>在执行器事务内稳定锁定来源、核验执行关联、整批回退并追加业务审计。</summary>
    private async Task<RollbackMaterialDemandsResult> ReverseInTransactionAsync(
        RollbackMaterialDemandsCommand command, CancellationToken cancellationToken)
    {
        var ordered = command.Items.OrderBy(item => item.GrossDemandId).ToArray();
        var demands = new List<GrossDemand>(ordered.Length);
        foreach (var item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            demands.Add(await grossDemandRepository.GetByIdForUpdateAsync(item.GrossDemandId)
                ?? throw new MaterialDemandException(DemandPlanningErrorCodes.SourceNotFound, "来源需求不存在或已删除。"));
        }

        var result = new RollbackMaterialDemandsResult();
        foreach (var pair in ordered.Zip(demands))
        {
            var item = pair.First;
            var demand = pair.Second;
            var active = await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
            if (active.Count == 0)
            {
                result.AlreadyRolledBackCount++;
                continue;
            }
            if (active.Any(row => row.DecompositionRevision != item.DecompositionRevision))
                throw new MaterialDemandException(DemandPlanningErrorCodes.RevisionConflict, "活动物料需求修订已变化，请刷新后重试。");
            var blockers = await executionLinkRepository.GetRollbackBlockersAsync(active.Select(row => row.Uid));
            if (blockers.Count > 0)
                throw new MaterialDemandException(DemandPlanningErrorCodes.DownstreamExecutionExists, "物料需求已有活动执行单，不能回退。");
            if (await netDemandRepository.RollbackRevisionAsync(demand.Uid, item.DecompositionRevision, command.Reason.Trim()) != active.Count)
                throw new MaterialDemandException(ServiceErrorCodes.ConcurrencyConflict, "物料需求已变化，回退未执行。");
            result.RolledBackSourceCount++;
        }

        await WriteLogAsync(command.CommandId, command.Reason.Trim(), result,
            ordered.Select(item => item.GrossDemandId).ToArray());
        return result;
    }

    /// <summary>业务审计记录回退原因、来源及数量，不承担回执重放。</summary>
    private async Task WriteLogAsync(Guid commandId, string reason, RollbackMaterialDemandsResult result, Guid[] sourceIds)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationContext.CorrelationId)
                ? commandId.ToString("N") : correlationContext.CorrelationId[..Math.Min(64, correlationContext.CorrelationId.Length)],
            Module = Module, AggregateType = "GrossDemand", AggregateUid = sourceIds[0],
            OperationType = RollbackOperation, OperationName = "回退来源物料需求", Reason = reason,
            Summary = $"回退 {result.RolledBackSourceCount} 项来源；已有 {result.AlreadyRolledBackCount} 项。",
            ChangedFieldsJson = JsonSerializer.Serialize(new { result.RolledBackSourceCount, result.AlreadyRolledBackCount }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { SourceIds = sourceIds }),
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new MaterialDemandException(ServiceErrorCodes.LogWriteFailed, "物料需求业务审计写入失败。");
    }

    /// <summary>登录及独立回退权限先于输入验证、回执查找和业务事务。</summary>
    private async Task<ServiceResult<RollbackMaterialDemandsResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<RollbackMaterialDemandsResult>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, CommandType, token) ? null
            : ServiceResult<RollbackMaterialDemandsResult>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有物料需求回退权限。");
    }

    /// <summary>统一返回验证、业务及执行器错误；取消仍由调用方处理。</summary>
    private async Task<ServiceResult<RollbackMaterialDemandsResult>> RunCommandAsync(
        Func<Task<ServiceResult<RollbackMaterialDemandsResult>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex) { return ServiceResult<RollbackMaterialDemandsResult>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<RollbackMaterialDemandsResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (MaterialDemandException ex) { return ServiceResult<RollbackMaterialDemandsResult>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<MaterialDemandReversalService>().LogError(ex, "回退来源物料需求失败。");
            return ServiceResult<RollbackMaterialDemandsResult>.Fail(ServiceErrorCodes.Unexpected, "回退来源物料需求失败。");
        }
    }

}
