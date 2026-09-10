using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>
/// 兼容旧混合接口；物料查询转发新服务，计划及采购命令等待调用方迁移后退出。
/// </summary>
public sealed class NetDemandWorkspaceService(
    ServiceBaseContext context,
    INetDemandServices materialDemands,
    INetDemandRepository netDemandRepository,
    INetDemandAllocationRepository allocationRepository,
    INetDemandPlanCommandRepository commandRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IProductRepository productRepository,
    ISupplierRepository supplierRepository,
    NetDemandPlanReplacementPlanner replacementPlanner,
    NetDemandPlanCommandIdempotencyPolicy idempotencyPolicy) : INetDemandWorkspaceService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<NetDemandWorkspaceService> _logger = context.LoggerFactory.CreateLogger<NetDemandWorkspaceService>();

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.SearchSourceDemandCandidatesAsync。</summary>
    public Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default)
        => materialDemands.SearchSourceDemandCandidatesAsync(request, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.EstablishFromSourcesAsync。</summary>
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => materialDemands.EstablishFromSourcesAsync(command, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.RollbackSourceEstablishmentAsync。</summary>
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => materialDemands.RollbackSourceEstablishmentAsync(command, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.SearchAsync。</summary>
    public Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default)
        => materialDemands.SearchAsync(request, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.SearchSourceGroupsAsync。</summary>
    public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
        => materialDemands.SearchSourceGroupsAsync(request, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.GetDetailAsync。</summary>
    public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => materialDemands.GetDetailAsync(uid, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.GetTraceAsync。</summary>
    public Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
        => materialDemands.GetTraceAsync(uid, cancellationToken);

    /// <summary>兼容旧入口，已迁移至 INetDemandServices.GetSourceDemandTraceAsync。</summary>
    public Task<ServiceResult<GrossDemandTraceDto>> GetSourceDemandTraceAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
        => materialDemands.GetSourceDemandTraceAsync(grossDemandId, cancellationToken);

    /// <summary>待废弃：旧 Allocation 替代命令，独立计划退出后移除。</summary>
    public async Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(ReplaceNetDemandPlanCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty)
            return ServiceResult<NetDemandPlanCommandResult>.Fail("命令标识不能为空。");
        var validation = ValidateSingleCommand(command.AllocationId, command.ExpectedVersion, command.Reason);
        if (validation is not null) return ServiceResult<NetDemandPlanCommandResult>.Fail(validation);
        if (!Enum.IsDefined(command.ExecutionType))
            return ServiceResult<NetDemandPlanCommandResult>.Fail("执行方式无效。");
        var result = new NetDemandPlanCommandResult();
        result.Items.Add(await ExecuteMutationAsync(new MutationRequest
        {
            CommandId = command.CommandId,
            AllocationId = command.AllocationId,
            ExpectedVersion = command.ExpectedVersion,
            Mode = MutationMode.Replace,
            ExecutionType = command.ExecutionType,
            SupplierId = command.PurchaseSupplierId,
            PlannedDeliveryDate = command.PlannedDeliveryDate,
            Reason = command.Reason.Trim()
        }));
        return ServiceResult<NetDemandPlanCommandResult>.Ok(result);
    }

    /// <summary>待废弃：旧计划应用默认供应商，实际供应商选择归采购。</summary>
    public async Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
        ApplyProductDefaultSupplierCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty)
            return ServiceResult<NetDemandPlanCommandResult>.Fail("命令标识不能为空。");
        var validation = ValidateSingleCommand(command.AllocationId, command.ExpectedVersion, command.Reason);
        if (validation is not null) return ServiceResult<NetDemandPlanCommandResult>.Fail(validation);
        var result = new NetDemandPlanCommandResult();
        result.Items.Add(await ExecuteMutationAsync(new MutationRequest
        {
            CommandId = command.CommandId,
            AllocationId = command.AllocationId,
            ExpectedVersion = command.ExpectedVersion,
            Mode = MutationMode.ApplyProductDefault,
            Reason = command.Reason.Trim()
        }));
        return ServiceResult<NetDemandPlanCommandResult>.Ok(result);
    }

    /// <summary>待废弃：旧计划批量分配供应商，实际供应商选择归采购。</summary>
    public async Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
        BatchAssignSupplierCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty || command.SupplierId == Guid.Empty)
            return ServiceResult<NetDemandPlanCommandResult>.Fail("命令标识和供应商不能为空。");
        var reason = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            return ServiceResult<NetDemandPlanCommandResult>.Fail("变更原因不能为空且不能超过 500 个字符。");
        if (command.Items is null || command.Items.Count == 0)
            return ServiceResult<NetDemandPlanCommandResult>.Fail("至少选择一条净需求计划。");
        if (command.Items.Any(x => x.AllocationId == Guid.Empty || x.ExpectedVersion <= 0)
            || command.Items.GroupBy(x => x.AllocationId).Any(x => x.Count() > 1))
            return ServiceResult<NetDemandPlanCommandResult>.Fail("批量命令包含无效或重复的计划分配。");

        var result = new NetDemandPlanCommandResult();
        var replacedByEarlierItem = new HashSet<Guid>();
        foreach (var item in command.Items.OrderBy(x => x.RowIndex))
        {
            var itemResult = await ExecuteMutationAsync(new MutationRequest
            {
                CommandId = command.CommandId,
                AllocationId = item.AllocationId,
                ExpectedVersion = item.ExpectedVersion,
                ClientKey = item.ClientKey,
                RowIndex = item.RowIndex,
                Mode = MutationMode.AssignSupplier,
                SupplierId = command.SupplierId,
                Reason = reason,
                ResolveSupersededFromSameBatch = replacedByEarlierItem.Contains(item.AllocationId)
            });
            result.Items.Add(itemResult);
            if (itemResult.Outcome != NetDemandCommandOutcomeEnum.Failed)
                replacedByEarlierItem.UnionWith(itemResult.AffectedCustomerSuppliedAllocationIds);
        }
        return ServiceResult<NetDemandPlanCommandResult>.Ok(result);
    }

    private async Task<NetDemandPlanCommandItemResult> ExecuteMutationAsync(MutationRequest request)
    {
        var result = new NetDemandPlanCommandItemResult
        {
            AllocationId = request.AllocationId,
            ClientKey = request.ClientKey,
            RowIndex = request.RowIndex,
            Outcome = NetDemandCommandOutcomeEnum.Failed
        };
        if (_session.Transaction is not null)
        {
            result.ErrorCode = "TRANSACTION_ALREADY_ACTIVE";
            result.Message = "当前请求已存在未完成事务。";
            return result;
        }

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 首次读取只用于定位；正式业务锁序统一为 GrossDemand -> NetDemand family -> Allocation -> Link。
            var submittedAllocation = await allocationRepository.GetByIdAsync(request.AllocationId)
                ?? throw new PlanCommandException("NOT_FOUND", "计划分配不存在或已删除。");
            var familyDemands = await netDemandRepository.GetActiveFamilyForUpdateAsync(submittedAllocation.NetDemandId);
            if (familyDemands.Count == 0)
                throw new PlanCommandException("CORRUPTED_STATE", "计划关联的活动毛需求/净需求不存在。");
            var familyAllocations = await allocationRepository.GetActiveByNetDemandIdsForUpdateAsync(
                familyDemands.Select(x => x.Uid));
            if (familyAllocations.Count != familyDemands.Count
                || familyAllocations.GroupBy(x => x.NetDemandId).Any(x => x.Count() != 1))
                throw new PlanCommandException("CORRUPTED_STATE", "A/B 净需求的活动计划不完整。");

            var fingerprint = idempotencyPolicy.BuildFingerprint(
                request.Mode.ToString(), request.AllocationId, request.ExpectedVersion,
                request.ExecutionType, request.SupplierId, request.PlannedDeliveryDate, request.Reason);
            var priorCommand = await commandRepository.GetForUpdateAsync(request.CommandId, request.AllocationId);
            if (priorCommand is not null)
            {
                var replay = idempotencyPolicy.CreateReplay(priorCommand, fingerprint, request.ClientKey, request.RowIndex);
                if (!replay.Success)
                    throw new PlanCommandException(replay.ErrorCode!, replay.ErrorMessage!);
                result = replay.Result!;
                _session.Transaction.Commit();
                return result;
            }

            NetDemandAllocation current;
            var submittedActive = familyAllocations.SingleOrDefault(x => x.Uid == submittedAllocation.Uid);
            if (submittedActive is not null)
            {
                if (submittedActive.Version != request.ExpectedVersion)
                    throw new PlanCommandException("CONCURRENCY_CONFLICT", "计划已被其他用户修改，请刷新后重试。");
                current = submittedActive;
            }
            else if (request.ResolveSupersededFromSameBatch
                     && submittedAllocation.LifecycleStatus == NetDemandAllocationLifecycleStatusEnum.Superseded)
            {
                current = familyAllocations.Single(x => x.NetDemandId == submittedAllocation.NetDemandId);
            }
            else
            {
                throw new PlanCommandException("CONCURRENCY_CONFLICT", "计划已被其他用户修改，请刷新后重试。");
            }
            var targetDemand = familyDemands.SingleOrDefault(x => x.Uid == current.NetDemandId)
                ?? throw new PlanCommandException("CORRUPTED_STATE", "计划关联的活动净需求不存在。");

            var desiredExecution = current.ExecutionType;
            var desiredSupplierId = current.PurchaseSupplierId;
            var desiredDate = current.PlannedDeliveryDate;
            Supplier? desiredSupplier = null;
            switch (request.Mode)
            {
                case MutationMode.Replace:
                    desiredExecution = request.ExecutionType!.Value;
                    if (targetDemand.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier
                        && desiredExecution != DemandExecutionTypeEnum.Purchase)
                        throw new PlanCommandException("CUSTOMER_SUPPLIED_PURCHASE_REQUIRED", "客供件 B 只能保持采购执行方式。");
                    desiredSupplierId = desiredExecution == DemandExecutionTypeEnum.Purchase ? request.SupplierId : null;
                    desiredDate = request.PlannedDeliveryDate;
                    if (desiredExecution != DemandExecutionTypeEnum.Purchase && request.SupplierId.HasValue)
                        throw new PlanCommandException("INVALID_SUPPLIER", "生产或外协计划不能设置采购供应商。");
                    break;
                case MutationMode.ApplyProductDefault:
                    if (current.ExecutionType != DemandExecutionTypeEnum.Purchase)
                        throw new PlanCommandException("INVALID_EXECUTION_TYPE", "只有采购型计划可以应用产品默认供应商。");
                    if (!targetDemand.ProductId.HasValue)
                        throw new PlanCommandException("PRODUCT_NOT_FOUND", "非标净需求没有产品默认供应商。");
                    var product = await productRepository.GetByIdAsync(targetDemand.ProductId.Value, withUpdLock: true)
                        ?? throw new PlanCommandException("PRODUCT_NOT_FOUND", "净需求关联产品不存在或已删除。");
                    desiredSupplierId = product.DefaultSupplierId
                        ?? throw new PlanCommandException("DEFAULT_SUPPLIER_MISSING", "产品尚未设置有效默认供应商。");
                    desiredSupplier = await supplierRepository.GetByIdAsync(desiredSupplierId.Value, withUpdLock: true)
                        ?? throw new PlanCommandException("DEFAULT_SUPPLIER_INVALID", "产品默认供应商已失效。");
                    break;
                case MutationMode.AssignSupplier:
                    if (current.ExecutionType != DemandExecutionTypeEnum.Purchase)
                        throw new PlanCommandException("INVALID_EXECUTION_TYPE", "只有采购型计划可以批量分配供应商。");
                    desiredSupplierId = request.SupplierId;
                    break;
            }

            if (desiredExecution == DemandExecutionTypeEnum.Purchase && desiredSupplierId.HasValue && desiredSupplier is null)
                desiredSupplier = await supplierRepository.GetByIdAsync(desiredSupplierId.Value, withUpdLock: true)
                    ?? throw new PlanCommandException("SUPPLIER_NOT_FOUND", "采购供应商不存在或已删除。");

            var supplierSnapshotChanged = desiredSupplier is not null
                && (!string.Equals(current.SupplierCodeSnapshot, desiredSupplier.SupplierCode, StringComparison.Ordinal)
                    || !string.Equals(current.SupplierNameSnapshot, desiredSupplier.SupplierName, StringComparison.Ordinal));
            var hasChange = desiredExecution != current.ExecutionType
                || desiredSupplierId != current.PurchaseSupplierId
                || supplierSnapshotChanged
                || desiredDate != current.PlannedDeliveryDate;
            if (!hasChange)
            {
                result.NewAllocationId = current.Uid;
                result.NewAllocationVersion = current.Version;
                result.Outcome = NetDemandCommandOutcomeEnum.AlreadyApplied;
                result.Message = "计划已是目标值。";
                await PersistCommandResultAsync(request, fingerprint, result);
                _session.Transaction.Commit();
                return result;
            }

            var deliveryTargetChanged = targetDemand.DeliveryMode == DemandDeliveryModeEnum.Standard;
            var affectedOldAllocations = new List<NetDemandAllocation> { current };
            if (deliveryTargetChanged)
            {
                var childDemandIds = familyDemands
                    .Where(x => x.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier
                                && x.DeliveryTargetNetDemandId == targetDemand.Uid)
                    .Select(x => x.Uid)
                    .ToHashSet();
                affectedOldAllocations.AddRange(familyAllocations.Where(x => childDemandIds.Contains(x.NetDemandId)));
            }
            var blockers = await executionLinkRepository.GetActiveByAllocationIdsForUpdateAsync(
                affectedOldAllocations.Select(x => x.Uid));
            if (blockers.Count > 0)
                throw new PlanCommandException("ACTIVE_EXECUTION_LINK_EXISTS", "相关计划已有活动执行单，必须先取消安全草稿或完成反向处理。", blockers);

            var assignmentSource = desiredExecution != DemandExecutionTypeEnum.Purchase || !desiredSupplierId.HasValue
                ? SupplierAssignmentSourceEnum.Unassigned
                : request.Mode == MutationMode.ApplyProductDefault
                    ? SupplierAssignmentSourceEnum.AppliedProductDefault
                    : desiredSupplierId == current.PurchaseSupplierId
                        ? current.AssignmentSource
                        : current.PurchaseSupplierId.HasValue
                            ? SupplierAssignmentSourceEnum.Reassigned
                            : SupplierAssignmentSourceEnum.Manual;
            var plan = replacementPlanner.Build(
                targetDemand, current, familyDemands, familyAllocations, desiredExecution,
                desiredSupplier, desiredDate, assignmentSource, request.Reason);
            if (!plan.IsValid)
                throw new PlanCommandException(plan.ErrorCode!, plan.ErrorMessage!);

            foreach (var replacement in plan.Replacements)
            {
                var old = familyAllocations.Single(x => x.Uid == replacement.ReplacesAllocationId);
                var superseded = await allocationRepository.SupersedeAsync(old.Uid, old.Version, request.Reason);
                if (!superseded.Success)
                    throw new PlanCommandException(superseded.ErrorCode ?? "CONCURRENCY_CONFLICT", superseded.Message ?? "计划替代失败。");
            }
            foreach (var replacement in plan.Replacements)
            {
                var inserted = await allocationRepository.AddReplacementAsync(replacement);
                if (!inserted.Success)
                    throw new PlanCommandException(inserted.ErrorCode ?? "INSERT_FAILED", inserted.Message ?? "新计划写入失败。");
            }

            var targetReplacement = plan.Replacements[0];
            result.NewAllocationId = targetReplacement.Uid;
            result.NewAllocationVersion = targetReplacement.Version;
            result.AffectedCustomerSuppliedAllocationIds = plan.AffectedCustomerSuppliedAllocationIds.ToList();
            result.Outcome = NetDemandCommandOutcomeEnum.Succeeded;
            result.Message = "计划替代成功。";
            await PersistCommandResultAsync(request, fingerprint, result);
            _session.Transaction.Commit();
            return result;
        }
        catch (PlanCommandException ex)
        {
            _session.Transaction.Rollback();
            result.ErrorCode = ex.Code;
            result.Message = ex.Message;
            result.Blockers = ex.Blockers.ToList();
            return result;
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "替代净需求计划 {AllocationId} 失败。", request.AllocationId);
            result.ErrorCode = "UNEXPECTED_ERROR";
            result.Message = "替代净需求计划失败。";
            return result;
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private static string? ValidateSingleCommand(Guid allocationId, int expectedVersion, string? reason)
    {
        if (allocationId == Guid.Empty || expectedVersion <= 0) return "计划分配和版本不能为空。";
        var normalized = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "变更原因不能为空。";
        return normalized.Length > 500 ? "变更原因不能超过 500 个字符。" : null;
    }

    private async Task PersistCommandResultAsync(
        MutationRequest request,
        string fingerprint,
        NetDemandPlanCommandItemResult result)
    {
        var write = await commandRepository.AddAsync(new NetDemandPlanCommandRecord
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CommandId = request.CommandId,
            OriginalAllocationId = request.AllocationId,
            CommandType = request.Mode.ToString(),
            RequestFingerprint = fingerprint,
            Outcome = result.Outcome,
            NewAllocationId = result.NewAllocationId,
            NewAllocationVersion = result.NewAllocationVersion,
            ResultMessage = result.Message,
            AffectedAllocationIds = result.AffectedCustomerSuppliedAllocationIds.Count == 0
                ? null
                : string.Join(",", result.AffectedCustomerSuppliedAllocationIds)
        });
        if (!write.Success)
            throw new PlanCommandException(write.ErrorCode ?? "IDEMPOTENCY_WRITE_FAILED", write.Message ?? "命令幂等结果写入失败。");
    }

    private enum MutationMode { Replace, ApplyProductDefault, AssignSupplier }

    private sealed class MutationRequest
    {
        public Guid CommandId { get; set; }
        public Guid AllocationId { get; set; }
        public int ExpectedVersion { get; set; }
        public string? ClientKey { get; set; }
        public int RowIndex { get; set; }
        public MutationMode Mode { get; set; }
        public DemandExecutionTypeEnum? ExecutionType { get; set; }
        public Guid? SupplierId { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool ResolveSupersededFromSameBatch { get; set; }
    }

    private sealed class PlanCommandException(
        string code,
        string message,
        IReadOnlyList<NetDemandExecutionLinkDto>? blockers = null) : Exception(message)
    {
        public string Code { get; } = code;
        public IReadOnlyList<NetDemandExecutionLinkDto> Blockers { get; } = blockers ?? [];
    }
}
