using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using System.Text.Json;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Reception;

/// <summary>在销售或售后外层事务内接收成品需求，维护版本与安全退出规则并记录业务审计。</summary>
public sealed class SourceDemandReceptionService(
    IDbSession session,
    IGrossDemandDocumentRepository grossDemandRepository,
    IMaterialDemandSourceExitService materialDemandSourceExit,
    ISourceDeliveryService sourceDeliveryService,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser) : ISourceDemandReceptionService, ISourceDemandTransactionPreparation
{
    /// <summary>只转交来源资源准备，不能提前业务验证或生成需求；过渡期原SQL仓储仍使用其事务内范围保护。</summary>
    public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default)
        => grossDemandRepository is ISourceDemandTransactionPreparation preparation
            ? preparation.AcquireBeforeTransactionAsync(sourceUids, grossDemandIds, cancellationToken)
            : Task.FromResult<IAsyncDisposable?>(OriginalTransactionPreparation.Instance);

    /// <summary>仅保留未切换SQL仓储原有事务行为；目标PG接线完成后删除这段过渡，不建立双库框架。</summary>
    private sealed class OriginalTransactionPreparation : IAsyncDisposable
    {
        internal static readonly OriginalTransactionPreparation Instance = new();
        /// <summary>原仓储没有事务外资源，无须提前释放或改变父事务。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private const string Module = "SourceDemand";
    private const string ReceiveOperation = "Receive";

    /// <summary>只参加调用方事务；父命令持有完整回执，本服务按来源事实与版本规则接收需求。</summary>
    public async Task<SourceDemandReceptionResult> ReceiveAsync(SourceDemandReceptionCommand command)
    {
        EnsureTransaction();
        Validate(command);
        command.SourceType = command.SourceType.Trim().ToUpperInvariant();
        command.SourceNo = command.SourceNo.Trim();
        var existing = await grossDemandRepository.GetActiveBySourceForUpdateAsync(command.SourceType, command.SourceUid);
        var duplicate = existing.GroupBy(item => item.SourceLineId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.DuplicateActiveSource, $"来源行 {duplicate.Key} 存在重复活动需求。");
        // SO/CR 的现行写入均带来源版本；活动旧事实缺失版本须人工核查，不能自动补齐。
        if (existing.Any(item => !item.SourceVersion.HasValue))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceVersionMissing, "活动销售或售后成品需求缺少来源版本，请核查历史事实后再确认。");
        if (command.RejectUnsubmittedActiveLines)
        {
            var submittedLineIds = command.Items.Select(item => item.SourceLineId).ToHashSet();
            if (existing.Any(item => !submittedLineIds.Contains(item.SourceLineId)))
                throw new SourceDemandReceptionException(DemandPlanningErrorCodes.UnsubmittedActiveSource, "来源仍存在本次未提交的活动需求行，不能确认。");
        }

        var existingByLine = existing.ToDictionary(item => item.SourceLineId);
        var result = new SourceDemandReceptionResult { SourceVersion = command.SourceVersion };
        foreach (var item in command.Items.OrderBy(item => item.SourceLineNumber).ThenBy(item => item.SourceLineId))
        {
            if (!existingByLine.TryGetValue(item.SourceLineId, out var demand))
            {
                var created = CreateDemand(command, item);
                EnsureWrite((await grossDemandRepository.AddSourceDemandsAsync([created])).Single());
                result.CreatedCount++;
                continue;
            }

            if (demand.SourceVersion > command.SourceVersion)
                throw new SourceDemandReceptionException(DemandPlanningErrorCodes.StaleSourceVersion, "来源版本早于已接收版本，不能覆盖当前需求。");

            var contentMatches = ContentMatches(demand, command, item);
            if (demand.SourceVersion == command.SourceVersion)
            {
                if (!contentMatches)
                    throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceVersionConflict, "同一来源版本提交了不同的需求内容。");
                result.UnchangedCount++;
                continue;
            }

            if (!contentMatches)
            {
                try
                {
                    await VerifyExitAsync(command.SourceType, command.SourceUid, [demand]);
                }
                catch (SourceDemandReceptionException ex)
                {
                    throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceChangeRequiresDisposition,
                        $"成品需求已有物料或交付历史，必须先完成影响处置：{ex.Message}");
                }
            }

            // 来源只更新自身事实；物料建立状态由 NetDemand 历史联表判断，不再回写兼容字段。
            Apply(command, item, demand);
            EnsureWrite(await grossDemandRepository.UpdateSourceDemandAsync(demand, demand.Version));
            result.UpdatedCount++;
        }

        await WriteLogAsync(command, result);
        return result;
    }

    /// <summary>组合物料与交付退出证明，并按物料历史决定硬删除或作废 GrossDemand。</summary>
    public async Task<SourceDemandExitResult> CancelConfirmationAsync(
        string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason)
    {
        EnsureTransaction();
        var demands = await grossDemandRepository.GetActiveBySourceForUpdateAsync(sourceType, sourceUid);
        var requestedLineIds = sourceLineIds.OrderBy(id => id).ToArray();
        var actualLineIds = demands.Select(item => item.SourceLineId).OrderBy(id => id).ToArray();
        if (requestedLineIds.Length != requestedLineIds.Distinct().Count()
            || demands.GroupBy(item => item.SourceLineId).Any(group => group.Count() != 1)
            || !actualLineIds.SequenceEqual(requestedLineIds))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceDemandMismatch, "成品需求数量与来源明细不一致。");
        var assessments = await VerifyExitAsync(sourceType, sourceUid, demands);

        SourceDemandCancellationPlan plan;
        try
        {
            plan = SourceDemandCancellationPolicy.Build(
                demands,
                assessments.ToDictionary(pair => pair.Key, pair => pair.Value.HistoryCount));
        }
        catch (InvalidOperationException ex)
        {
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceHistoryInvalid, ex.Message);
        }

        var deleted = await grossDemandRepository.HardDeleteForSourceCancellationAsync(plan.HardDeleteGrossDemandIds);
        var voided = await grossDemandRepository.VoidRolledBackForSourceCancellationAsync(plan.VoidGrossDemandIds, reason);
        if (deleted != plan.HardDeleteGrossDemandIds.Count || voided != plan.VoidGrossDemandIds.Count)
            throw new SourceDemandReceptionException(ServiceErrorCodes.ConcurrencyConflict, "成品需求状态已变化，退出确认失败。");
        return new SourceDemandExitResult { HardDeletedCount = deleted, VoidedCount = voided };
    }

    /// <summary>仅关闭无下游阻断的待处理成品需求，并核对实际影响行数。</summary>
    public async Task<int> ClosePendingAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds, string reason)
    {
        EnsureTransaction();
        var demands = await grossDemandRepository.GetActiveBySourceForUpdateAsync(sourceType, sourceUid);
        if (demands.Count == 0) return 0;
        await VerifyExitAsync(sourceType, sourceUid, demands);
        var affected = await grossDemandRepository.VoidPendingBySourceAsync(sourceType, sourceUid, reason);
        if (affected != demands.Count)
            throw new SourceDemandReceptionException(ServiceErrorCodes.ConcurrencyConflict, "部分成品需求状态已变化，关闭失败。");
        return affected;
    }

    /// <summary>在调用方事务中锁定成品需求并判断是否存在活动记录。</summary>
    public async Task<bool> HasActiveAsync(string sourceType, Guid sourceUid)
    {
        EnsureTransaction();
        return (await grossDemandRepository.GetActiveBySourceForUpdateAsync(sourceType, sourceUid)).Count > 0;
    }

    /// <summary>
    /// 在同一来源事务中组合物料历史证明与 Shipping 交付证明；任一证明缺失均失败关闭。
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> VerifyExitAsync(
        string sourceType,
        Guid sourceUid,
        IReadOnlyCollection<GrossDemand> demands)
    {
        var ids = demands.Select(item => item.Uid).OrderBy(id => id).ToArray();
        var assessments = await materialDemandSourceExit.AssessSourceExitAsync(ids);
        if (assessments.Count != ids.Length || ids.Any(id => !assessments.ContainsKey(id)))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceHistoryInvalid,
                "成品需求的物料历史评估不完整，不能退出确认。");
        if (assessments.Values.Any(item => item.HasActiveRevision))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceExitBlocked,
                "来源仍存在活动物料修订，请先在物料需求中完成安全回退。");
        if (assessments.Values.Any(item => !item.IsHistoryComplete))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceHistoryInvalid,
                "成品需求的物料历史不完整，不能退出确认。");
        try
        {
            await sourceDeliveryService.VerifySourceExitAsync(sourceType, sourceUid, ids);
        }
        catch (SourceDeliveryException ex)
        {
            throw new SourceDemandReceptionException(ex.Code, ex.Message);
        }
        return assessments;
    }

    private static GrossDemand CreateDemand(SourceDemandReceptionCommand command, SourceDemandReceptionItem item)
    {
        var demand = new GrossDemand { Uid = GuidSequentialGenerator.NewGuid() };
        Apply(command, item, demand);
        return demand;
    }

    /// <summary>应用来源事实快照；物料建立状态和修订历史不再写回成品需求。</summary>
    private static void Apply(
        SourceDemandReceptionCommand command,
        SourceDemandReceptionItem item,
        GrossDemand demand)
    {
        demand.LifecycleStatus = GrossDemandLifecycleStatusEnum.Active;
        demand.SourceType = command.SourceType.Trim();
        demand.SourceUid = command.SourceUid;
        demand.SourceNo = command.SourceNo.Trim();
        demand.SourceLineId = item.SourceLineId;
        demand.SourceLineNumber = item.SourceLineNumber;
        demand.SourceVersion = command.SourceVersion;
        demand.CustomerId = command.CustomerId;
        demand.ProductId = item.ProductId;
        demand.IsNonStandard = item.IsNonStandard;
        demand.SkuSnapshot = item.SkuSnapshot.Trim();
        demand.LinkSkuSnapshot = Normalize(item.LinkSkuSnapshot);
        demand.CustomerSkuSnapshot = Normalize(item.CustomerSkuSnapshot);
        demand.DescriptionSnapshot = Normalize(item.DescriptionSnapshot);
        demand.ColorSnapshot = Normalize(item.ColorSnapshot);
        demand.SpecificationSnapshot = Normalize(item.SpecificationSnapshot);
        demand.ImagePathSnapshot = Normalize(item.ImagePathSnapshot);
        demand.UnitSnapshot = Normalize(item.UnitSnapshot);
        demand.DemandQty = item.DemandQty;
        demand.RequiredDate = item.RequiredDate;
        demand.Remark = Normalize(item.Remark);
        demand.DemandBasisFingerprint = SourceDemandBasisFingerprintFactory.Create(demand);
    }

    private static bool ContentMatches(GrossDemand demand, SourceDemandReceptionCommand command, SourceDemandReceptionItem item)
        => demand.SourceLineNumber == item.SourceLineNumber
           && demand.CustomerId == command.CustomerId
           && demand.ProductId == item.ProductId
           && demand.IsNonStandard == item.IsNonStandard
           && TextEquals(demand.SourceNo, command.SourceNo)
           && TextEquals(demand.SkuSnapshot, item.SkuSnapshot)
           && TextEquals(demand.LinkSkuSnapshot, item.LinkSkuSnapshot)
           && TextEquals(demand.CustomerSkuSnapshot, item.CustomerSkuSnapshot)
           && TextEquals(demand.DescriptionSnapshot, item.DescriptionSnapshot)
           && TextEquals(demand.ColorSnapshot, item.ColorSnapshot)
           && TextEquals(demand.SpecificationSnapshot, item.SpecificationSnapshot)
           && TextEquals(demand.ImagePathSnapshot, item.ImagePathSnapshot)
           && TextEquals(demand.UnitSnapshot, item.UnitSnapshot)
           && demand.DemandQty == item.DemandQty
           && demand.RequiredDate == item.RequiredDate
           && TextEquals(demand.Remark, item.Remark);

    /// <summary>校验来源身份、版本和完整行集合；拒绝无效请求，不从提示文本推断错误类别。</summary>
    private static void Validate(SourceDemandReceptionCommand command)
    {
        if (command is null) throw new SourceDemandReceptionException(ServiceErrorCodes.InvalidCommand, "成品需求命令不能为空。");
        if (command.CommandId == Guid.Empty || command.SourceUid == Guid.Empty || command.CustomerId == Guid.Empty || command.SourceVersion <= 0)
            throw new SourceDemandReceptionException(ServiceErrorCodes.InvalidCommand, "成品需求命令身份、客户或版本无效。");
        if (string.IsNullOrWhiteSpace(command.SourceType) || string.IsNullOrWhiteSpace(command.SourceNo) || command.Items.Count == 0)
            throw new SourceDemandReceptionException(ServiceErrorCodes.InvalidCommand, "来源类型、来源单号和需求行不能为空。");
        var sourceType = command.SourceType.Trim().ToUpperInvariant();
        if (sourceType is not (DocumentTypeStaticsEnum.SalesOrder or DocumentTypeStaticsEnum.ComplaintReplenishment))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.SourceTypeNotSupported, "接收协议仅接受销售或投诉补货认可的实物需求。");
        if (command.Items.Any(item => item.SourceLineId == Guid.Empty || item.DemandQty <= 0 || string.IsNullOrWhiteSpace(item.SkuSnapshot)))
            throw new SourceDemandReceptionException(ServiceErrorCodes.InvalidCommand, "成品需求行身份、SKU 或数量无效。");
        if (command.Items.GroupBy(item => item.SourceLineId).Any(group => group.Count() > 1))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.DuplicateSourceLine, "同一命令不能重复提交来源行。");
        if (command.Items.Any(item => item.IsNonStandard && item.ProductId.HasValue))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.NonStandardProductForbidden, "非标需求不能关联产品主数据。");
        if (command.Items.Any(item => (!item.IsNonStandard && !item.ProductId.HasValue) || string.IsNullOrWhiteSpace(item.UnitSnapshot)))
            throw new SourceDemandReceptionException(DemandPlanningErrorCodes.ProductUnitRequired, "标准需求必须提供产品，所有实物需求必须提供明确单位。");
    }

    /// <summary>记录首次父命令在同一事务内产生的来源数量和行身份，不承担机器回执。</summary>
    private async Task WriteLogAsync(SourceDemandReceptionCommand command, SourceDemandReceptionResult result)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = command.CommandId,
            CorrelationId = Normalize(correlationContext.CorrelationId, 64) ?? command.CommandId.ToString("N"),
            Module = Module, AggregateType = "SourceDemand", AggregateUid = command.SourceUid,
            AggregateNoSnapshot = command.SourceNo, OperationType = ReceiveOperation, OperationName = "接收成品需求",
            Summary = $"来源版本 {command.SourceVersion}：新增 {result.CreatedCount}，更新 {result.UpdatedCount}，未变化 {result.UnchangedCount}。",
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                command.SourceVersion, result.CreatedCount, result.UpdatedCount, result.UnchangedCount
            }),
            RelatedObjectsJson = JsonSerializer.Serialize(new
            {
                command.SourceType, command.SourceUid,
                SourceLineIds = command.Items.Select(item => item.SourceLineId).OrderBy(id => id)
            }),
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new SourceDemandReceptionException(ServiceErrorCodes.LogWriteFailed, "成品需求审计写入失败。");
    }

    /// <summary>来源接收必须参与上游事务，不能独立提交来源或需求事实。</summary>
    private void EnsureTransaction()
    {
        if (session.Transaction is null)
            throw new SourceDemandReceptionException(ServiceErrorCodes.TransactionRequired, "成品需求接收必须加入调用方业务事务。");
    }

    /// <summary>透传仓储稳定错误码；缺少写入错误时保留原技术失败代码。</summary>
    private static void EnsureWrite(Contracts.Wrappers.RepositoryWriteResult result)
    {
        if (!result.Success) throw new SourceDemandReceptionException(result.ErrorCode ?? ServiceErrorCodes.WriteFailed, result.Message ?? "成品需求写入失败。");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = Normalize(value);
        return normalized is null || normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
    private static bool TextEquals(string? left, string? right) => string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}
