using Microsoft.Extensions.Logging;
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

/// <summary>整批安全回退来源物料修订，检查活动执行关联并保留现有审计重放。</summary>
public sealed class MaterialDemandReversalService(
    ServiceBaseContext context,
    IGrossDemandRepository grossDemandRepository,
    INetDemandRepository netDemandRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser) : IMaterialDemandReversalService
{
    private const string Module = "MaterialDemand";
    private const string RollbackOperation = "RollbackSourceEstablishment";
    private readonly MaterialDemandCommandSupport _commandSupport = new(
        context.Session, operationLogs, correlationContext, currentUser,
        context.LoggerFactory.CreateLogger<MaterialDemandReversalService>());

    /// <summary>回退选定来源的活动物料修订；活动执行关系存在时整批命令回滚。</summary>
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => _commandSupport.ExecuteAsync(async () =>
        {
            ValidateRollback(command);
            var ordered = command.Items.OrderBy(item => item.GrossDemandId).ToArray();
            var demands = new List<GrossDemand>(ordered.Length);
            foreach (var item in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                demands.Add(await grossDemandRepository.GetByIdForUpdateAsync(item.GrossDemandId)
                    ?? throw new MaterialDemandException("SOURCE_NOT_FOUND", "来源需求不存在或已删除。"));
            }

            var fingerprint = MaterialDemandCommandSupport.CreateFingerprint(command);
            var prior = await operationLogs.GetByCommandAsync(Module, RollbackOperation, command.CommandId);
            if (prior is not null)
                return MaterialDemandCommandSupport.Replay<RollbackMaterialDemandsResult>(prior, fingerprint);

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
                    throw new MaterialDemandException("REVISION_CONFLICT", "活动物料需求修订已变化，请刷新后重试。");
                var blockers = await executionLinkRepository.GetRollbackBlockersAsync(active.Select(row => row.Uid));
                if (blockers.Count > 0)
                    throw new MaterialDemandException("DOWNSTREAM_EXECUTION_EXISTS", "物料需求已有活动执行单，不能回退。");
                if (await netDemandRepository.RollbackRevisionAsync(demand.Uid, item.DecompositionRevision, command.Reason.Trim()) != active.Count)
                    throw new MaterialDemandException("CONCURRENCY_CONFLICT", "物料需求已变化，回退未执行。");
                result.RolledBackSourceCount++;
            }

            await _commandSupport.WriteLogAsync(command.CommandId, RollbackOperation, "回退来源物料需求",
                fingerprint, result, ordered[0].GrossDemandId, command.Reason.Trim());
            return result;
        }, "回退来源物料需求失败", cancellationToken);

    /// <summary>沿用现有原因、修订和重复来源校验，保持原错误码。</summary>
    private static void ValidateRollback(RollbackMaterialDemandsCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty || command.Items.Count == 0)
            throw new MaterialDemandException("INVALID_COMMAND", "命令标识和回退项不能为空。");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 500)
            throw new MaterialDemandException("INVALID_REASON", "回退原因不能为空且不能超过 500 个字符。");
        if (command.Items.Any(item => item.GrossDemandId == Guid.Empty || item.DecompositionRevision <= 0)
            || command.Items.GroupBy(item => item.GrossDemandId).Any(group => group.Count() > 1))
            throw new MaterialDemandException("INVALID_COMMAND", "命令包含无效或重复的物料修订。");
    }

}
