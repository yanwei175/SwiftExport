using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Services.DemandPlanning.Materials;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;

/// <summary>将来源需求整批建立为物料事实，按稳定来源顺序锁定并保留现有审计重放。</summary>
public sealed class MaterialDemandEstablishmentService(
    ServiceBaseContext context,
    IGrossDemandRepository grossDemandRepository,
    INetDemandRepository netDemandRepository,
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
    private readonly MaterialDemandCommandSupport _commandSupport = new(
        context.Session, operationLogs, correlationContext, currentUser,
        context.LoggerFactory.CreateLogger<MaterialDemandEstablishmentService>());

    /// <summary>
    /// 在单一事务中按稳定顺序锁定来源需求，复核版本后复用既有 BOM 规划器建立完整物料依据。
    /// </summary>
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => _commandSupport.ExecuteAsync(async () =>
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

            var fingerprint = MaterialDemandCommandSupport.CreateFingerprint(command);
            var prior = await operationLogs.GetByCommandAsync(Module, EstablishOperation, command.CommandId);
            if (prior is not null)
                return MaterialDemandCommandSupport.Replay<EstablishMaterialDemandsResult>(prior, fingerprint);

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

            await _commandSupport.WriteLogAsync(command.CommandId, EstablishOperation, "从来源需求建立物料需求",
                fingerprint, result, ordered[0].GrossDemandId, null);
            return result;
        }, "从来源需求建立物料需求失败", cancellationToken);

    /// <summary>在既有事务中锁定根产品和 BOM 子产品，交给纯规划器生成待写入物料行。</summary>
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
            var childIds = boms.Where(line => line.ComponentProductId.HasValue).Select(line => line.ComponentProductId!.Value).Distinct().ToArray();
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

    /// <summary>校验来源生命周期和两个独立版本，避免基于旧来源事实建立物料需求。</summary>
    private static void EnsureSourceVersion(GrossDemand demand, int expectedDemandVersion, int? expectedSourceVersion)
    {
        if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active)
            throw new MaterialDemandException("INVALID_SOURCE_LIFECYCLE", "只有活动来源需求可以建立物料需求。");
        if (demand.Version != expectedDemandVersion || demand.SourceVersion != expectedSourceVersion)
            throw new MaterialDemandException("SOURCE_CHANGED", "来源需求版本已变化，请刷新候选后重试。");
    }

    /// <summary>将任一仓储写入失败提升为整批业务失败。</summary>
    private static void EnsureWrites(IReadOnlyList<RepositoryWriteResult> writes)
    {
        var failed = writes.FirstOrDefault(result => !result.Success);
        if (failed is not null)
            throw new MaterialDemandException(failed.ErrorCode ?? "WRITE_FAILED", failed.Message ?? "物料需求写入失败。");
    }

    /// <summary>沿用命令身份、来源版本和重复来源检查，保持原错误码。</summary>
    private static void ValidateEstablish(EstablishMaterialDemandsCommand command)
    {
        if (command is null || command.CommandId == Guid.Empty || command.Items.Count == 0)
            throw new MaterialDemandException("INVALID_COMMAND", "命令标识和来源需求不能为空。");
        if (command.Items.Any(item => item.GrossDemandId == Guid.Empty || item.SourceDemandVersion <= 0)
            || command.Items.GroupBy(item => item.GrossDemandId).Any(group => group.Count() > 1))
            throw new MaterialDemandException("INVALID_COMMAND", "命令包含无效或重复的来源需求。");
    }

    private sealed record PlanContext(
        IReadOnlyList<NetDemand> NetDemands,
        IReadOnlyDictionary<Guid, Product> Products);

}
