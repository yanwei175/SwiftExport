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
    MaterialDemandSuggestedSupplierInitializer suggestedSupplierInitializer,
    IPermissionEvaluationService permissions, CommandPipeline pipeline, BusinessCommandExecutor executor) : IMaterialDemandEstablishmentService
{
    private const string Module = "MaterialDemand";
    private const string EstablishOperation = "EstablishFromSource";
    private const string CommandType = PermissionKeys.DemandPlanningMaterialEstablish;

    /// <summary>
    /// 在单一事务中按稳定顺序锁定来源需求，复核版本后复用既有 BOM 规划器建立完整物料依据。
    /// </summary>
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default)
        => RunCommandAsync(() => pipeline.ExecuteAsync<EstablishMaterialDemandsCommand, EstablishMaterialDemandsResult>(
            command, command?.CommandId, CommandType, AuthorizeAsync,
            token => ExecuteEstablishCommandAsync(command!, token), cancellationToken));

    /// <summary>通过公共执行器在 Serializable 事务中核对共享组成与来源事实，排序后的完整集合参与公共指纹。</summary>
    private async Task<ServiceResult<CommandExecutionResult<EstablishMaterialDemandsResult>>> ExecuteEstablishCommandAsync(
        EstablishMaterialDemandsCommand command, CancellationToken token)
    {
        var input = command.Items.OrderBy(item => item.GrossDemandId)
            .Select(item => new { item.GrossDemandId, item.SourceDemandVersion, item.SourceVersion }).ToArray();
        var result = await executor.ExecuteAsync(command.CommandId, CommandType, null, new { Items = input },
            ct => EstablishInTransactionAsync(command, ct), token, isolationLevel: IsolationLevel.Serializable,
            beforeTransaction: grossDemandRepository is ISourceDemandTransactionPreparation preparation
                ? ct => preparation.AcquireBeforeTransactionAsync([], input.Select(x => x.GrossDemandId).ToArray(), ct) : null);
        return ServiceResult<CommandExecutionResult<EstablishMaterialDemandsResult>>.Ok(result);
    }

    /// <summary>在执行器拥有的单一事务中稳定锁定来源、重验版本、写入物料事实及业务审计。</summary>
    private async Task<EstablishMaterialDemandsResult> EstablishInTransactionAsync(
        EstablishMaterialDemandsCommand command, CancellationToken cancellationToken)
    {
        var ordered = command.Items.OrderBy(item => item.GrossDemandId).ToArray();
        var demands = new List<GrossDemand>(ordered.Length);
        foreach (var item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            demands.Add(await grossDemandRepository.GetByIdForUpdateAsync(item.GrossDemandId)
                ?? throw new MaterialDemandException("SOURCE_NOT_FOUND", "来源需求不存在或已删除。"));
        }

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
            throw new MaterialDemandException("LOG_WRITE_FAILED", "物料需求业务审计写入失败。");
    }

    /// <summary>登录及独立建立权限先于输入验证、回执查找和业务事务。</summary>
    private async Task<ServiceResult<EstablishMaterialDemandsResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, CommandType, token) ? null
            : ServiceResult<EstablishMaterialDemandsResult>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有物料需求建立权限。");
    }

    /// <summary>将验证、业务及公共执行器错误转为既有服务响应；取消继续向调用方传播。</summary>
    private async Task<ServiceResult<EstablishMaterialDemandsResult>> RunCommandAsync(
        Func<Task<ServiceResult<EstablishMaterialDemandsResult>>> action)
    {
        try { return await action(); }
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

    /// <summary>在既有事务中锁定真实根及直接子产品，按根的有效编号读取共享组成，交给纯规划器生成待写入物料行。</summary>
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
                boms = (await bomRepository.GetBomByBomNoAsync(root.BomNo)).ToArray();
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

    private sealed record PlanContext(
        IReadOnlyList<NetDemand> NetDemands,
        IReadOnlyDictionary<Guid, Product> Products);

}
