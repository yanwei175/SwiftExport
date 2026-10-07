using SwiftExport.Contracts.DTOs.DemandPlanning;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using System.Data;
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

/// <summary>将来源需求整批建立为物料事实；公共命令组件负责授权边界、事务及回执。</summary>
public sealed class MaterialDemandEstablishmentService(
    ServiceBaseContext context,
    IGrossDemandDocumentRepository grossDemandRepository,
    IMaterialDemandDocumentRepository netDemandRepository,
    IProductRepository productRepository,
    ISupplierRepository supplierRepository,
    IBomRepository bomRepository,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlationContext,
    ICurrentUser currentUser,
    GrossDemandDecompositionPlanner planner,
    IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : IMaterialDemandEstablishmentService
{
    private const string Module = "MaterialDemand";
    private const string EstablishOperation = "EstablishFromSource";
    private const string CommandType = PermissionKeys.DemandPlanningMaterialEstablish;

    /// <summary>
    /// 先授权、验证请求，再交执行器准备资源并开启 Serializable 事务；业务主线见 EstablishInTransactionAsync。
    /// 执行器负责回执重放、统一提交及失败回滚，本入口只转换既有错误，取消继续传播。
    /// </summary>
    public async Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pipeline.ExecuteAsync<EstablishMaterialDemandsCommand, EstablishMaterialDemandsResult>(
                command, command?.CommandId, CommandType, AuthorizeAsync,
                async token =>
                {
                    // 授权和请求验证完成后才规范化输入；字段、排序及匿名对象形状保持原指纹协议。
                    var input = command!.Items.OrderBy(item => item.GrossDemandId)
                        .Select(item => new { item.GrossDemandId, item.SourceDemandVersion, item.SourceVersion }).ToArray();
                    // 命令租约由执行器取得；来源资源也必须在事务和首次快照之前准备。
                    // 回执重放不进入业务主线；首次执行在物料及审计写入后追加回执，由执行器统一提交。
                    var result = await executor.ExecuteAsync(command.CommandId, CommandType, null, new { Items = input },
                        ct => EstablishInTransactionAsync(command, ct), token, isolationLevel: IsolationLevel.Serializable,
                        beforeTransaction: grossDemandRepository is ISourceDemandTransactionPreparation preparation
                            ? ct => preparation.AcquireBeforeTransactionAsync([], input.Select(x => x.GrossDemandId).ToArray(), ct) : null);
                    return ServiceResult<CommandExecutionResult<EstablishMaterialDemandsResult>>.Ok(result);
                }, cancellationToken);
        }
        catch (CommandValidationException ex) { return ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<EstablishMaterialDemandsResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (MaterialDemandException ex) { return ServiceResult<EstablishMaterialDemandsResult>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<MaterialDemandEstablishmentService>().LogError(ex, "从来源需求建立物料需求失败。");
            return ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Unexpected, "从来源需求建立物料需求失败。");
        }
    }

    /// <summary>在执行器拥有的单一事务中稳定锁定来源、重验版本、写入物料事实及业务审计。</summary>
    private async Task<EstablishMaterialDemandsResult> EstablishInTransactionAsync(
        EstablishMaterialDemandsCommand command, CancellationToken cancellationToken)
    {
        // 先按稳定顺序锁完整来源集合，再逐项校验和建立；不得交错为逐来源独立事务。
        var ordered = command.Items.OrderBy(item => item.GrossDemandId).ToArray();
        var demands = new List<GrossDemand>(ordered.Length);
        foreach (var item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            demands.Add(await grossDemandRepository.GetByIdForUpdateAsync(item.GrossDemandId)
                ?? throw new MaterialDemandException(DemandPlanningErrorCodes.SourceNotFound, "来源需求不存在或已删除。"));
        }

        var result = new EstablishMaterialDemandsResult();
        foreach (var pair in ordered.Zip(demands))
        {
            var item = pair.First;
            var demand = pair.Second;
            // 活动生命周期及两个版本通过后，检查本来源已有活动物料的依据是否仍一致。
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
                    DemandPlanningErrorCodes.SourceChangeRequiresDisposition,
                    $"来源需求 {demand.SourceNo} 第 {demand.SourceLineNumber} 行已变化，必须先处置当前物料需求。");
            }

            // 锁定物料修订历史，再锁产品并读取有效共享组成；规划器只根据这些事实生成待写物料。
            var nextRevision = await netDemandRepository.GetNextDecompositionRevisionForUpdateAsync(demand.Uid);
            var composition = await ReadProductCompositionAsync(demand);
            var decomposition = planner.BuildPlan(demand, composition.Root, composition.BomLines, composition.Children, nextRevision);
            if (!decomposition.IsValid)
                throw new MaterialDemandException(decomposition.ErrorCode!, decomposition.ErrorMessage!);
            foreach (var row in decomposition.NetDemands)
            {
                row.SourceDemandVersionSnapshot = demand.Version;
                row.SourceVersionSnapshot = demand.SourceVersion;
                row.SourceDemandBasisFingerprintSnapshot = currentBasis;
            }
            // 来源快照已确定；每条物料只使用自身产品的有效默认供应商作为初始建议。
            var allProducts = composition.Children.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (composition.Root is not null) allProducts[composition.Root.Uid] = composition.Root;
            var supplierIds = allProducts.Values.Where(product => product.DefaultSupplierId.HasValue)
                .Select(product => product.DefaultSupplierId!.Value).Distinct().ToArray();
            var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds, withUpdLock: true))
                .ToDictionary(supplier => supplier.Uid);
            InitializeSuggestedSuppliers(decomposition.NetDemands, allProducts, suppliers);

            // 只写物料分解事实，不生成采购、贡献关系或库存；任一写入失败交执行器整批回滚。
            EnsureWrites(await netDemandRepository.AddDecompositionAsync(decomposition.NetDemands));
            result.CreatedSourceCount++;
            result.CreatedMaterialDemandCount += decomposition.NetDemands.Count;
        }

        // 业务审计与物料共用父事务；返回后执行器追加命令回执，最后只提交一次。
        await WriteLogAsync(command.CommandId, result, ordered.Select(item => item.GrossDemandId).ToArray());
        return result;
    }

    /// <summary>业务审计仅记录操作、来源和数量，不保存机器回执或输入指纹。</summary>
    private async Task WriteLogAsync(Guid commandId, EstablishMaterialDemandsResult result, Guid[] sourceIds)
    {
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationContext.CorrelationId)
                ? commandId.ToString("N") : correlationContext.CorrelationId[..Math.Min(64, correlationContext.CorrelationId.Length)],
            Module = Module, AggregateType = "GrossDemand", AggregateUid = sourceIds[0],
            OperationType = EstablishOperation, OperationName = "从来源需求建立物料需求",
            Summary = $"建立 {result.CreatedSourceCount} 项来源、{result.CreatedMaterialDemandCount} 行物料需求；已有 {result.AlreadyEstablishedCount} 项。",
            ChangedFieldsJson = JsonSerializer.Serialize(new { result.CreatedSourceCount, result.CreatedMaterialDemandCount, result.AlreadyEstablishedCount }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { SourceIds = sourceIds }),
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1)
            throw new MaterialDemandException(ServiceErrorCodes.LogWriteFailed, "物料需求业务审计写入失败。");
    }

    /// <summary>登录及独立建立权限先于输入验证、回执查找和业务事务。</summary>
    private async Task<ServiceResult<EstablishMaterialDemandsResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, CommandType, token) ? null
            : ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有物料需求建立权限。");
    }

    /// <summary>在父事务中依次锁根产品、读取有效编号共享组成、锁直接子产品并校验客供终端；非标返回空组成，不规划或写入物料。</summary>
    private async Task<ProductComposition> ReadProductCompositionAsync(GrossDemand demand)
    {
        Product? root = null;
        IReadOnlyList<Bom> boms = [];
        IReadOnlyDictionary<Guid, Product> children = new Dictionary<Guid, Product>();
        if (!demand.IsNonStandard)
        {
            if (!demand.ProductId.HasValue)
                throw new MaterialDemandException(DemandPlanningErrorCodes.ProductNotFound, "标准来源需求没有关联产品。");
            root = await productRepository.GetByIdAsync(demand.ProductId.Value, withUpdLock: true)
                ?? throw new MaterialDemandException(DemandPlanningErrorCodes.ProductNotFound, "来源需求关联产品不存在或已删除。");
            if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(root))
                throw new MaterialDemandException(DemandPlanningErrorCodes.CustomerSuppliedRootNotAllowed, "客供件不能作为标准来源需求根产品。");
            if (root.BomVersion > 0 && !string.IsNullOrWhiteSpace(root.BomNo))
                boms = (await bomRepository.GetBomByBomNoAsync(root.BomNo)).ToArray();
            var childIds = boms.Where(line => line.ComponentProductId.HasValue).Select(line => line.ComponentProductId!.Value).Distinct().ToArray();
            children = (await productRepository.GetByIdListAsync(childIds, withUpdLock: true)).ToDictionary(product => product.Uid);
            foreach (var child in children.Values.Where(CustomerSuppliedMaterialPolicy.IsCustomerSupplied))
            {
                var error = CustomerSuppliedMaterialPolicy.ValidateProduct(
                    child, await bomRepository.HasActiveBomForProductAsync(child.Uid, child.BomNo));
                if (error is not null)
                    throw new MaterialDemandException(DemandPlanningErrorCodes.InvalidCustomerSuppliedProduct, error);
            }
        }

        return new ProductComposition(root, boms, children);
    }

    /// <summary>校验来源生命周期和两个独立版本，避免基于旧来源事实建立物料需求。</summary>
    private static void EnsureSourceVersion(GrossDemand demand, int expectedDemandVersion, int? expectedSourceVersion)
    {
        if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active)
            throw new MaterialDemandException(DemandPlanningErrorCodes.InvalidSourceLifecycle, "只有活动来源需求可以建立物料需求。");
        if (demand.Version != expectedDemandVersion || demand.SourceVersion != expectedSourceVersion)
            throw new MaterialDemandException(DemandPlanningErrorCodes.SourceChanged, "来源需求版本已变化，请刷新候选后重试。");
    }

    /// <summary>将任一仓储写入失败提升为整批业务失败。</summary>
    private static void EnsureWrites(IReadOnlyList<RepositoryWriteResult> writes)
    {
        var failed = writes.FirstOrDefault(result => !result.Success);
        if (failed is not null)
            throw new MaterialDemandException(failed.ErrorCode ?? ServiceErrorCodes.WriteFailed, failed.Message ?? "物料需求写入失败。");
    }

    /// <summary>局部初始化各行自己的采购供应商建议；无产品、无默认或默认供应商失效时保持为空。</summary>
    private static void InitializeSuggestedSuppliers(
        IReadOnlyList<NetDemand> demands,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Supplier> suppliers)
    {
        foreach (var demand in demands)
        {
            demand.SuggestedPurchaseSupplierId = demand.ProductId is Guid productId
                && products.TryGetValue(productId, out var product)
                && product.DefaultSupplierId is Guid supplierId
                && suppliers.ContainsKey(supplierId)
                    ? supplierId
                    : null;
        }
    }

    /// <summary>仅携带本用例已锁定读取的组成事实，规划与持久化仍由事务主线明确执行。</summary>
    private sealed record ProductComposition(
        Product? Root,
        IReadOnlyList<Bom> BomLines,
        IReadOnlyDictionary<Guid, Product> Children);

}
