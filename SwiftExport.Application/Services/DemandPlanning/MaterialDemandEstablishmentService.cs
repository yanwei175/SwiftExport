using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>
/// 负责在物料领域内把来源需求原子建立为物料需求事实，并以物料历史和有效执行关系决定修订及回退。
/// </summary>
public sealed class MaterialDemandEstablishmentService(
    ServiceBaseContext context,
    IGrossDemandRepository grossDemandRepository,
    INetDemandRepository netDemandRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IProductRepository productRepository,
    ISupplierRepository supplierRepository,
    IBomRepository bomRepository,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser,
    GrossDemandDecompositionPlanner planner,
    MaterialDemandSuggestedSupplierInitializer suggestedSupplierInitializer) : IMaterialDemandEstablishmentService
{
    private const string Module = "MaterialDemand";
    private const string EstablishOperation = "EstablishFromSource";
    private const string RollbackOperation = "RollbackSourceEstablishment";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<MaterialDemandEstablishmentService> _logger =
        context.LoggerFactory.CreateLogger<MaterialDemandEstablishmentService>();

    /// <summary>查询由物料领域联表计算的来源需求候选。</summary>
    public async Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new SourceDemandCandidateSearchRequest();
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
            return ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>.Ok(
                await netDemandRepository.SearchSourceDemandCandidatesAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源需求建立候选失败。");
            return ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>.Fail("查询来源需求建立候选失败。");
        }
    }

    /// <summary>
    /// 在单一事务中按稳定顺序锁定来源需求，复核版本后复用既有 BOM 规划器建立完整物料依据。
    /// </summary>
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            ValidateEstablish(command);
            var ordered = command.Items.OrderBy(item => item.GrossDemandId).ToArray();
            var demands = new List<GrossDemand>(ordered.Length);
            foreach (var item in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                demands.Add(await grossDemandRepository.GetByIdForUpdateAsync(item.GrossDemandId)
                    ?? throw new MaterialDemandException("SOURCE_NOT_FOUND", "来源需求不存在或已删除。"));
            }

            var fingerprint = CreateFingerprint(command);
            var prior = await operationLogs.GetByCommandAsync(Module, EstablishOperation, command.CommandId);
            if (prior is not null)
                return Replay<EstablishMaterialDemandsResult>(prior, fingerprint);

            var result = new EstablishMaterialDemandsResult();
            foreach (var pair in ordered.Zip(demands))
            {
                var item = pair.First;
                var demand = pair.Second;
                EnsureSourceVersion(demand, item.SourceDemandVersion, item.SourceVersion);
                var currentBasis = demand.DemandBasisFingerprint ?? SourceDemandBasisFingerprintFactory.Create(demand);
                var active = await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
                if (active.Count > 0)
                {
                    if (active.All(row => string.Equals(
                            row.SourceDemandBasisFingerprintSnapshot, currentBasis, StringComparison.Ordinal)))
                    {
                        result.AlreadyEstablishedCount++;
                        continue;
                    }
                    throw new MaterialDemandException(
                        "SOURCE_CHANGE_REQUIRES_DISPOSITION",
                        $"来源需求 {demand.SourceNo} 第 {demand.SourceLineNumber} 行已变化，必须先处置当前物料需求。");
                }

                var nextRevision = await netDemandRepository.GetNextDecompositionRevisionForUpdateAsync(demand.Uid);
                var decomposition = await BuildPlanAsync(demand, nextRevision);
                foreach (var row in decomposition.NetDemands)
                {
                    row.SourceDemandVersionSnapshot = demand.Version;
                    row.SourceVersionSnapshot = demand.SourceVersion;
                    row.SourceDemandBasisFingerprintSnapshot = currentBasis;
                }
                var allProducts = decomposition.Products;
                var supplierIds = allProducts.Values.Where(product => product.DefaultSupplierId.HasValue)
                    .Select(product => product.DefaultSupplierId!.Value).Distinct().ToArray();
                var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds, withUpdLock: true))
                    .ToDictionary(supplier => supplier.Uid);
                suggestedSupplierInitializer.Apply(decomposition.NetDemands, allProducts, suppliers);

                EnsureWrites(await netDemandRepository.AddDecompositionAsync(decomposition.NetDemands));
                result.CreatedSourceCount++;
                result.CreatedMaterialDemandCount += decomposition.NetDemands.Count;
            }

            await WriteLogAsync(command.CommandId, EstablishOperation, "从来源需求建立物料需求",
                fingerprint, result, ordered[0].GrossDemandId, null);
            return result;
        }, "从来源需求建立物料需求失败", cancellationToken);

    /// <summary>回退选定来源的活动物料修订；活动执行关系存在时整批命令回滚。</summary>
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
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

            var fingerprint = CreateFingerprint(command);
            var prior = await operationLogs.GetByCommandAsync(Module, RollbackOperation, command.CommandId);
            if (prior is not null)
                return Replay<RollbackMaterialDemandsResult>(prior, fingerprint);

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
                var blockers = await executionLinkRepository.GetActiveRollbackBlockersAsync(active.Select(row => row.Uid));
                if (blockers.Count > 0)
                    throw new MaterialDemandException("DOWNSTREAM_EXECUTION_EXISTS", "物料需求已有活动执行单，不能回退。");
                if (await netDemandRepository.RollbackRevisionAsync(demand.Uid, item.DecompositionRevision, command.Reason.Trim()) != active.Count)
                    throw new MaterialDemandException("CONCURRENCY_CONFLICT", "物料需求已变化，回退未执行。");
                result.RolledBackSourceCount++;
            }

            await WriteLogAsync(command.CommandId, RollbackOperation, "回退来源物料需求",
                fingerprint, result, ordered[0].GrossDemandId, command.Reason.Trim());
            return result;
        }, "回退来源物料需求失败", cancellationToken);

    /// <summary>在既有事务内按来源需求锁定活动物料行，供来源本体写操作做边界校验。</summary>
    public async Task<bool> HasActiveAsync(Guid grossDemandId)
    {
        if (_session.Transaction is null)
            throw new InvalidOperationException("物料事实校验必须在调用方事务内执行。");
        return (await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(grossDemandId)).Count > 0;
    }

    /// <summary>在来源调用方事务内协调物料、执行及正式收发事实的退出阻断。</summary>
    public Task<bool> HasSourceExitBlockerAsync(string sourceType, Guid sourceUid, IReadOnlyCollection<Guid> sourceLineIds)
    {
        if (_session.Transaction is null) throw new InvalidOperationException("来源退出校验必须在调用方事务内执行。");
        return netDemandRepository.HasSourceExitBlockerAsync(sourceType, sourceUid, sourceLineIds);
    }

    /// <summary>在来源调用方事务内取得物料历史计数，不再依赖来源兼容修订字段。</summary>
    public Task<IReadOnlyDictionary<Guid, int>> GetHistoryCountsForUpdateAsync(IEnumerable<Guid> grossDemandIds)
    {
        if (_session.Transaction is null) throw new InvalidOperationException("来源退出校验必须在调用方事务内执行。");
        return netDemandRepository.GetHistoryCountsForUpdateAsync(grossDemandIds);
    }

    /// <summary>在来源调用方事务内验证物料历史已完整安全退出。</summary>
    public Task<bool> HasInvalidExitHistoryAsync(IEnumerable<Guid> grossDemandIds)
    {
        if (_session.Transaction is null) throw new InvalidOperationException("来源退出校验必须在调用方事务内执行。");
        return netDemandRepository.HasInvalidExitHistoryAsync(grossDemandIds);
    }

    private async Task<PlanContext> BuildPlanAsync(GrossDemand demand, int revision)
    {
        Product? root = null;
        IReadOnlyList<Bom> boms = [];
        IReadOnlyDictionary<Guid, Product> children = new Dictionary<Guid, Product>();
        if (!demand.IsNonStandard)
        {
            if (!demand.ProductId.HasValue)
                throw new MaterialDemandException("PRODUCT_NOT_FOUND", "标准来源需求没有关联产品。");
            root = await productRepository.GetByIdAsync(demand.ProductId.Value, withUpdLock: true)
                ?? throw new MaterialDemandException("PRODUCT_NOT_FOUND", "来源需求关联产品不存在或已删除。");
            if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(root))
                throw new MaterialDemandException("CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED", "客供件不能作为标准来源需求根产品。");
            if (root.BomVersion > 0 && !string.IsNullOrWhiteSpace(root.BomNo))
                boms = await bomRepository.GetByBomNoAndVersionAsync(root.BomNo, root.BomVersion);
            var childIds = boms.Where(line => line.SubSkuId.HasValue).Select(line => line.SubSkuId!.Value).Distinct().ToArray();
            children = (await productRepository.GetByIdListAsync(childIds, withUpdLock: true)).ToDictionary(product => product.Uid);
            foreach (var child in children.Values.Where(CustomerSuppliedMaterialPolicy.IsCustomerSupplied))
            {
                var error = CustomerSuppliedMaterialPolicy.ValidateProduct(
                    child, await bomRepository.HasActiveBomForProductAsync(child.Uid, child.BomNo));
                if (error is not null)
                    throw new MaterialDemandException("INVALID_CUSTOMER_SUPPLIED_PRODUCT", error);
            }
        }

        var plan = planner.BuildPlan(demand, root, boms, children, revision);
        if (!plan.IsValid)
            throw new MaterialDemandException(plan.ErrorCode!, plan.ErrorMessage!);
        var products = children.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (root is not null) products[root.Uid] = root;
        return new PlanContext(plan.NetDemands, products);
    }

    private static void EnsureSourceVersion(GrossDemand demand, int expectedDemandVersion, int? expectedSourceVersion)
    {
        if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active)
            throw new MaterialDemandException("INVALID_SOURCE_LIFECYCLE", "只有活动来源需求可以建立物料需求。");
        if (demand.Version != expectedDemandVersion || demand.SourceVersion != expectedSourceVersion)
            throw new MaterialDemandException("SOURCE_CHANGED", "来源需求版本已变化，请刷新候选后重试。");
    }

    private static void EnsureWrites(IReadOnlyList<RepositoryWriteResult> writes)
    {
        var failed = writes.FirstOrDefault(result => !result.Success);
        if (failed is not null)
            throw new MaterialDemandException(failed.ErrorCode ?? "WRITE_FAILED", failed.Message ?? "物料需求写入失败。");
    }

    private async Task<ServiceResult<T>> ExecuteAsync<T>(
        Func<Task<T>> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null)
            return ServiceResult<T>.Fail("TRANSACTION_ALREADY_ACTIVE", "当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var result = await action();
            _session.Transaction.Commit();
            return ServiceResult<T>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            throw;
        }
        catch (MaterialDemandException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<T>.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task WriteLogAsync<T>(
        Guid commandId, string operationType, string operationName, string fingerprint,
        T result, Guid aggregateId, string? reason)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationContext.CorrelationId)
                ? commandId.ToString("N") : correlationContext.CorrelationId[..Math.Min(64, correlationContext.CorrelationId.Length)],
            Module = Module, AggregateType = "GrossDemand", AggregateUid = aggregateId,
            OperationType = operationType, OperationName = operationName, Reason = reason,
            Summary = operationName, ChangedFieldsJson = JsonSerializer.Serialize(result), RelatedObjectsJson = fingerprint,
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new MaterialDemandException("LOG_WRITE_FAILED", "物料需求幂等日志写入失败。");
    }

    private static T Replay<T>(BusinessOperationLog prior, string fingerprint)
    {
        if (!string.Equals(prior.RelatedObjectsJson, fingerprint, StringComparison.Ordinal))
            throw new MaterialDemandException("COMMAND_REUSED", "同一 CommandId 已用于其他物料需求内容。");
        return JsonSerializer.Deserialize<T>(prior.ChangedFieldsJson ?? string.Empty)
            ?? throw new MaterialDemandException("IDEMPOTENCY_RESULT_INVALID", "历史幂等结果无法读取。");
    }

    private static string CreateFingerprint(object command)
    {
        // 幂等指纹按来源身份排序，并规范化原因；客户端选择顺序变化不应构成不同业务内容。
        var json = command switch
        {
            EstablishMaterialDemandsCommand establish => JsonSerializer.Serialize(new
            {
                Items = establish.Items.OrderBy(item => item.GrossDemandId).Select(item => new
                {
                    item.GrossDemandId, item.SourceDemandVersion, item.SourceVersion
                })
            }),
            RollbackMaterialDemandsCommand rollback => JsonSerializer.Serialize(new
            {
                Reason = rollback.Reason.Trim(),
                Items = rollback.Items.OrderBy(item => item.GrossDemandId).Select(item => new
                {
                    item.GrossDemandId, item.DecompositionRevision
                })
            }),
            _ => JsonSerializer.Serialize(command)
        };
        return JsonSerializer.Serialize(new
        {
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
        });
    }

    private static void ValidateEstablish(EstablishMaterialDemandsCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty || command.Items.Count == 0)
            throw new MaterialDemandException("INVALID_COMMAND", "命令标识和来源需求不能为空。");
        if (command.Items.Any(item => item.GrossDemandId == Guid.Empty || item.SourceDemandVersion <= 0)
            || command.Items.GroupBy(item => item.GrossDemandId).Any(group => group.Count() > 1))
            throw new MaterialDemandException("INVALID_COMMAND", "命令包含无效或重复的来源需求。");
    }

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

    private sealed record PlanContext(
        IReadOnlyList<NetDemand> NetDemands,
        IReadOnlyDictionary<Guid, Product> Products);

    private sealed class MaterialDemandException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
