using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.DemandPlanning;

public sealed class GrossDemandWorkspaceService(
    ServiceBaseContext context,
    IGrossDemandRepository grossDemandRepository,
    INetDemandRepository netDemandRepository,
    INetDemandAllocationRepository allocationRepository,
    IDemandExecutionLinkRepository executionLinkRepository,
    IProductRepository productRepository,
    ISupplierRepository supplierRepository,
    IBomRepository bomRepository,
    IDocumentNoService documentNoService,
    GrossDemandDecompositionPlanner planner,
    NetDemandInitialAllocationPlanner allocationPlanner) : IGrossDemandWorkspaceService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<GrossDemandWorkspaceService> _logger = context.LoggerFactory.CreateLogger<GrossDemandWorkspaceService>();

    public async Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new GrossDemandSearchRequest();
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(
                await grossDemandRepository.SearchWorkspaceAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询毛需求工作台失败。");
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail("查询毛需求失败。");
        }
    }

    public async Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<GrossDemandDetailDto>.Fail("毛需求 Uid 不能为空。");
        try
        {
            var detail = await grossDemandRepository.GetWorkspaceDetailAsync(uid, cancellationToken);
            return detail is null
                ? ServiceResult<GrossDemandDetailDto>.Fail("未找到毛需求。")
                : ServiceResult<GrossDemandDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载毛需求 {Uid} 失败。", uid);
            return ServiceResult<GrossDemandDetailDto>.Fail("加载毛需求失败。");
        }
    }

    public async Task<ServiceResult<GrossDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<GrossDemandTraceDto>.Fail("毛需求 Uid 不能为空。");
        try
        {
            var detail = await grossDemandRepository.GetWorkspaceDetailAsync(uid, cancellationToken);
            if (detail is null) return ServiceResult<GrossDemandTraceDto>.Fail("未找到毛需求。");
            var netDemands = await netDemandRepository.GetByGrossDemandForTraceAsync(uid, cancellationToken);
            var links = await executionLinkRepository.GetTraceByNetDemandIdsAsync(
                netDemands.Select(x => x.Uid), cancellationToken);
            return ServiceResult<GrossDemandTraceDto>.Ok(new GrossDemandTraceDto
            {
                GrossDemand = detail,
                NetDemands = [.. netDemands],
                ExecutionLinks = [.. links]
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载毛需求追溯 {Uid} 失败。", uid);
            return ServiceResult<GrossDemandTraceDto>.Fail("加载毛需求追溯失败。");
        }
    }

    public async Task<ServiceResult<GrossDemandDecomposeResult>> DecomposeAsync(GrossDemandDecomposeCommand command)
    {
        var items = command?.Items;
        var shapeError = ValidateCommandItems(items);
        if (shapeError is not null) return ServiceResult<GrossDemandDecomposeResult>.Fail(shapeError);
        var result = new GrossDemandDecomposeResult();
        foreach (var item in items!)
            result.Items.Add(await DecomposeOneAsync(item));
        return ServiceResult<GrossDemandDecomposeResult>.Ok(result);
    }

    public async Task<ServiceResult<GrossDemandRollbackResult>> RollbackAsync(GrossDemandRollbackCommand command)
    {
        var items = command?.Items;
        var shapeError = ValidateCommandItems(items);
        if (shapeError is not null) return ServiceResult<GrossDemandRollbackResult>.Fail(shapeError);
        var reason = command!.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) return ServiceResult<GrossDemandRollbackResult>.Fail("回退原因不能为空。");
        if (reason.Length > 500) return ServiceResult<GrossDemandRollbackResult>.Fail("回退原因不能超过 500 个字符。");
        var result = new GrossDemandRollbackResult();
        foreach (var item in items!)
            result.Items.Add(await RollbackOneAsync(item, reason));
        return ServiceResult<GrossDemandRollbackResult>.Ok(result);
    }

    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var uid = GuidSequentialGenerator.NewGuid();
            var demand = new GrossDemand
            {
                Uid = uid,
                SourceType = DocumentTypeStaticsEnum.ManualDemand,
                SourceUid = uid,
                SourceLineId = uid,
                LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
                DecompositionStatus = GrossDemandDecompositionStatusEnum.Pending,
                DecompositionRevision = 0
            };
            await ApplyManualChangesAsync(demand, request.ProductId, request.IsNonStandard, request.Sku,
                request.LinkSku, request.CustomerSku, request.Description, request.Color,
                request.Specification, request.ImagePath, request.DemandQty, request.RequiredDate, request.Remark);
            var number = await documentNoService.GetNextDocumentNoAsync(new DocumentNoRequest
            {
                DocType = DocumentTypeStaticsEnum.ManualDemand,
                BusinessDate = DateTime.Today
            });
            if (!number.Success || number.Data is null) throw new GrossDemandValidationException(number.Message);
            demand.SourceNo = number.Data.DocumentNo;
            EnsureWriteSucceeded((await grossDemandRepository.AddSourceDemandsAsync([demand])).Single());
            return await grossDemandRepository.GetWorkspaceDetailAsync(uid)
                ?? throw new GrossDemandValidationException("手工毛需求已创建，但重新加载失败。");
        }, "创建手工毛需求失败");

    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request)
        => ExecuteTransactionAsync(async () =>
        {
            if (request is null || uid == Guid.Empty || request.Uid != uid)
                throw new GrossDemandValidationException("路由 Uid 与请求体 Uid 不一致。");
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid)
                ?? throw new GrossDemandValidationException("手工毛需求不存在或已删除。");
            EnsureEditableManual(demand);
            if (demand.Version != request.Version)
                throw new GrossDemandValidationException("毛需求已被其他用户修改，请刷新后重试。");
            if ((await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(uid)).Count > 0)
                throw new GrossDemandValidationException("毛需求仍有活动净需求，请先完成拆解回退。");
            await ApplyManualChangesAsync(demand, request.ProductId, request.IsNonStandard, request.Sku,
                request.LinkSku, request.CustomerSku, request.Description, request.Color,
                request.Specification, request.ImagePath, request.DemandQty, request.RequiredDate, request.Remark);
            EnsureWriteSucceeded(await grossDemandRepository.UpdateManualAsync(demand, request.Version));
            return await grossDemandRepository.GetWorkspaceDetailAsync(uid)
                ?? throw new GrossDemandValidationException("手工毛需求已更新，但重新加载失败。");
        }, "更新手工毛需求失败");

    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command)
        => ExecuteTransactionAsync(async () =>
        {
            if (command is null || uid == Guid.Empty || command.Uid != uid)
                throw new GrossDemandValidationException("路由 Uid 与请求体 Uid 不一致。");
            var reason = command.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason)) throw new GrossDemandValidationException("关闭原因不能为空。");
            if (reason.Length > 500) throw new GrossDemandValidationException("关闭原因不能超过 500 个字符。");
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(uid)
                ?? throw new GrossDemandValidationException("手工毛需求不存在或已删除。");
            EnsureEditableManual(demand);
            if (demand.Version != command.Version)
                throw new GrossDemandValidationException("毛需求已被其他用户修改，请刷新后重试。");
            if ((await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(uid)).Count > 0)
                throw new GrossDemandValidationException("毛需求已拆解，请先完成拆解回退。");
            EnsureWriteSucceeded(await grossDemandRepository.CloseManualAsync(uid, command.Version, reason));
            return await grossDemandRepository.GetWorkspaceDetailAsync(uid)
                ?? throw new GrossDemandValidationException("手工毛需求已关闭，但重新加载失败。");
        }, "关闭手工毛需求失败");

    private async Task<GrossDemandDecomposeItemResult> DecomposeOneAsync(GrossDemandCommandItem item)
    {
        var result = new GrossDemandDecomposeItemResult
        {
            Uid = item.Uid, ClientKey = item.ClientKey, RowIndex = item.RowIndex,
            OldVersion = item.Version, NewVersion = item.Version,
            DecompositionRevision = item.ExpectedDecompositionRevision,
            Outcome = GrossDemandCommandOutcomeEnum.Failed
        };
        return await ExecuteItemTransactionAsync(async () =>
        {
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(item.Uid)
                ?? throw new GrossDemandItemException("NOT_FOUND", "毛需求不存在或已删除。");
            result.OldVersion = demand.Version;
            if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active)
                throw new GrossDemandItemException("INVALID_LIFECYCLE", "只有活动毛需求可以拆解。");
            var activeNetDemands = await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
            var activeAllocations = await allocationRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
            if (demand.DecompositionStatus == GrossDemandDecompositionStatusEnum.Decomposed)
            {
                if (demand.DecompositionRevision == item.ExpectedDecompositionRevision + 1 && activeNetDemands.Count > 0
                    && activeNetDemands.All(x => x.DecompositionRevision == demand.DecompositionRevision))
                {
                    var allocationError = ValidateActiveAllocations(activeNetDemands, activeAllocations);
                    if (allocationError is not null)
                        throw new GrossDemandItemException("CORRUPTED_STATE", allocationError);
                    result.Outcome = GrossDemandCommandOutcomeEnum.AlreadyApplied;
                    result.Message = "该拆解命令已成功应用。";
                    result.NewVersion = demand.Version;
                    result.DecompositionRevision = demand.DecompositionRevision;
                    result.CreatedNetDemandCount = activeNetDemands.Count;
                    return result;
                }
                throw new GrossDemandItemException("REVISION_CONFLICT", "毛需求已存在其他活动拆解版本，请刷新后重试。");
            }
            if (activeNetDemands.Count > 0 || activeAllocations.Count > 0)
                throw new GrossDemandItemException("CORRUPTED_STATE", "毛需求拆解状态与活动净需求/计划分配不一致，请联系管理员修复数据。");
            if (demand.Version != item.Version || demand.DecompositionRevision != item.ExpectedDecompositionRevision)
                throw new GrossDemandItemException("CONCURRENCY_CONFLICT", "毛需求已被其他用户修改，请刷新后重试。");

            Product? root = null;
            IReadOnlyList<Bom> boms = [];
            IReadOnlyDictionary<Guid, Product> children = new Dictionary<Guid, Product>();
            if (!demand.IsNonStandard)
            {
                if (!demand.ProductId.HasValue)
                    throw new GrossDemandItemException("PRODUCT_NOT_FOUND", "标准毛需求没有关联产品。");
                root = await productRepository.GetByIdAsync(demand.ProductId.Value, withUpdLock: true)
                    ?? throw new GrossDemandItemException("PRODUCT_NOT_FOUND", "毛需求关联产品不存在或已删除。");
                if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(root))
                    throw new GrossDemandItemException("CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED", "客供件不能作为标准毛需求根产品。");
                if (root.BomVersion > 0 && !string.IsNullOrWhiteSpace(root.BomNo))
                    boms = await bomRepository.GetByBomNoAndVersionAsync(root.BomNo, root.BomVersion);
                var childIds = boms.Where(x => x.SubSkuId.HasValue).Select(x => x.SubSkuId!.Value).Distinct().ToArray();
                children = (await productRepository.GetByIdListAsync(childIds, withUpdLock: true)).ToDictionary(x => x.Uid);
                foreach (var child in children.Values.Where(CustomerSuppliedMaterialPolicy.IsCustomerSupplied))
                {
                    var error = CustomerSuppliedMaterialPolicy.ValidateProduct(
                        child, await bomRepository.HasActiveBomForProductAsync(child.Uid, child.BomNo));
                    if (error is not null) throw new GrossDemandItemException("INVALID_CUSTOMER_SUPPLIED_PRODUCT", error);
                }
            }
            var nextRevision = demand.DecompositionRevision + 1;
            var plan = planner.BuildPlan(demand, root, boms, children, nextRevision);
            if (!plan.IsValid) throw new GrossDemandItemException(plan.ErrorCode!, plan.ErrorMessage!);

            var allProducts = children.ToDictionary(x => x.Key, x => x.Value);
            if (root is not null) allProducts[root.Uid] = root;
            var supplierIds = allProducts.Values
                .Where(x => x.DefaultSupplierId.HasValue)
                .Select(x => x.DefaultSupplierId!.Value)
                .Distinct()
                .ToArray();
            var suppliers = (await supplierRepository.GetByIdListAsync(supplierIds, withUpdLock: true))
                .ToDictionary(x => x.Uid);
            var initialPlan = allocationPlanner.Build(plan.NetDemands, allProducts, suppliers);
            if (!initialPlan.IsValid)
                throw new GrossDemandItemException(initialPlan.ErrorCode!, initialPlan.ErrorMessage!);

            var writes = await netDemandRepository.AddDecompositionAsync(plan.NetDemands);
            var writeFailure = writes.FirstOrDefault(x => !x.Success);
            if (writeFailure is not null)
                throw new GrossDemandItemException(writeFailure.ErrorCode ?? "INSERT_FAILED", writeFailure.Message);
            var allocationWrites = await allocationRepository.AddInitialAsync(initialPlan.Allocations);
            var allocationFailure = allocationWrites.FirstOrDefault(x => !x.Success);
            if (allocationFailure is not null)
                throw new GrossDemandItemException(allocationFailure.ErrorCode ?? "INSERT_FAILED", allocationFailure.Message);
            var stateWrite = await grossDemandRepository.UpdateDecompositionStateAsync(
                demand.Uid, demand.Version, GrossDemandDecompositionStatusEnum.Decomposed, nextRevision, DateTime.Now);
            EnsureItemWriteSucceeded(stateWrite);
            result.Outcome = GrossDemandCommandOutcomeEnum.Succeeded;
            var unassignedCount = initialPlan.Allocations.Count(x =>
                x.ExecutionType == DemandExecutionTypeEnum.Purchase && !x.PurchaseSupplierId.HasValue);
            result.Message = unassignedCount > 0
                ? $"拆解成功；{unassignedCount} 条采购计划待分配供应商。"
                : "拆解成功。";
            result.NewVersion = stateWrite.NewVersion ?? demand.Version + 1;
            result.DecompositionRevision = nextRevision;
            result.CreatedNetDemandCount = plan.NetDemands.Count;
            return result;
        }, result, "拆解毛需求失败");
    }

    private async Task<GrossDemandRollbackItemResult> RollbackOneAsync(GrossDemandCommandItem item, string reason)
    {
        var result = new GrossDemandRollbackItemResult
        {
            Uid = item.Uid, ClientKey = item.ClientKey, RowIndex = item.RowIndex,
            OldVersion = item.Version, NewVersion = item.Version,
            DecompositionRevision = item.ExpectedDecompositionRevision,
            Outcome = GrossDemandCommandOutcomeEnum.Failed
        };
        return await ExecuteItemTransactionAsync(async () =>
        {
            var demand = await grossDemandRepository.GetByIdForUpdateAsync(item.Uid)
                ?? throw new GrossDemandItemException("NOT_FOUND", "毛需求不存在或已删除。");
            result.OldVersion = demand.Version;
            var active = await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
            var activeAllocations = await allocationRepository.GetActiveByGrossDemandForUpdateAsync(demand.Uid);
            if (demand.DecompositionStatus == GrossDemandDecompositionStatusEnum.Pending && active.Count == 0
                && activeAllocations.Count == 0
                && demand.DecompositionRevision == item.ExpectedDecompositionRevision)
            {
                result.Outcome = GrossDemandCommandOutcomeEnum.AlreadyApplied;
                result.Message = "该拆解版本已回退。";
                result.NewVersion = demand.Version;
                result.DecompositionRevision = demand.DecompositionRevision;
                return result;
            }
            if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active
                || demand.DecompositionStatus != GrossDemandDecompositionStatusEnum.Decomposed)
                throw new GrossDemandItemException("INVALID_STATE", "当前毛需求状态不允许回退拆解。");
            if (demand.DecompositionRevision != item.ExpectedDecompositionRevision)
                throw new GrossDemandItemException("REVISION_CONFLICT", "活动拆解版本已变化，请刷新后重试。");
            if (demand.Version != item.Version)
                throw new GrossDemandItemException("CONCURRENCY_CONFLICT", "毛需求已被其他用户修改，请刷新后重试。");
            if (active.Count == 0 || active.Any(x => x.DecompositionRevision != demand.DecompositionRevision))
                throw new GrossDemandItemException("CORRUPTED_STATE", "毛需求拆解状态与活动净需求不一致，请联系管理员修复数据。");
            var allocationError = ValidateActiveAllocations(active, activeAllocations);
            if (allocationError is not null)
                throw new GrossDemandItemException("CORRUPTED_STATE", allocationError);
            var blockers = await executionLinkRepository.GetActiveRollbackBlockersAsync(active.Select(x => x.Uid));
            if (blockers.Count > 0)
            {
                result.ErrorCode = "DOWNSTREAM_EXECUTION_EXISTS";
                result.Message = "净需求已有下游执行单，不能回退。";
                result.Blockers = [.. blockers];
                return result;
            }
            var cancelled = await allocationRepository.CancelRevisionAsync(
                demand.Uid, demand.DecompositionRevision, "Rollback", reason);
            if (cancelled != activeAllocations.Count)
                throw new GrossDemandItemException("CONCURRENCY_CONFLICT", "计划分配版本已变化，回退未执行。");
            var rolledBack = await netDemandRepository.RollbackRevisionAsync(demand.Uid, demand.DecompositionRevision, reason);
            if (rolledBack != active.Count)
                throw new GrossDemandItemException("CONCURRENCY_CONFLICT", "净需求版本已变化，回退未执行。");
            var stateWrite = await grossDemandRepository.UpdateDecompositionStateAsync(
                demand.Uid, demand.Version, GrossDemandDecompositionStatusEnum.Pending,
                demand.DecompositionRevision, null);
            EnsureItemWriteSucceeded(stateWrite);
            result.Outcome = GrossDemandCommandOutcomeEnum.Succeeded;
            result.Message = "拆解回退成功。";
            result.NewVersion = stateWrite.NewVersion ?? demand.Version + 1;
            result.DecompositionRevision = demand.DecompositionRevision;
            return result;
        }, result, "回退毛需求拆解失败");
    }

    private async Task ApplyManualChangesAsync(
        GrossDemand demand, Guid? productId, bool isNonStandard, string? requestedSku,
        string? linkSku, string? customerSku, string? description, string? color,
        string? specification, string? imagePath, decimal quantity, DateTime? requiredDate, string? remark)
    {
        if (quantity <= 0) throw new GrossDemandValidationException("需求数量必须大于零。");
        demand.IsNonStandard = isNonStandard;
        demand.DemandQty = quantity;
        demand.RequiredDate = requiredDate;
        demand.Remark = remark?.Trim();
        demand.CustomerSkuSnapshot = customerSku?.Trim();
        if (isNonStandard)
        {
            if (productId.HasValue) throw new GrossDemandValidationException("非标件不能关联产品主数据。");
            if (string.IsNullOrWhiteSpace(description)) throw new GrossDemandValidationException("非标件描述不能为空。");
            demand.ProductId = null;
            demand.SkuSnapshot = NonStandardSkuGenerator.Create(demand.Uid);
            demand.LinkSkuSnapshot = linkSku?.Trim();
            demand.DescriptionSnapshot = description.Trim();
            demand.ColorSnapshot = color?.Trim();
            demand.SpecificationSnapshot = specification?.Trim();
            demand.ImagePathSnapshot = imagePath?.Trim();
            return;
        }
        if (!productId.HasValue || productId.Value == Guid.Empty)
            throw new GrossDemandValidationException("标准件必须选择产品。");
        var product = await productRepository.GetByIdAsync(productId.Value)
            ?? throw new GrossDemandValidationException("所选产品不存在或已删除。");
        if (CustomerSuppliedMaterialPolicy.IsCustomerSupplied(product))
            throw new GrossDemandValidationException("客供件不能作为手工毛需求根产品。");
        demand.ProductId = product.Uid;
        demand.SkuSnapshot = product.Sku;
        demand.LinkSkuSnapshot = product.LinkSku;
        demand.DescriptionSnapshot = product.DescriptionEn;
        demand.ColorSnapshot = color?.Trim();
        demand.SpecificationSnapshot = product.FeaturesEn;
        demand.ImagePathSnapshot = product.MainImage;
    }

    private async Task<T> ExecuteItemTransactionAsync<T>(Func<Task<T>> action, T failedResult, string logMessage)
        where T : class
    {
        if (_session.Transaction is not null) throw new InvalidOperationException("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var value = await action();
            _session.Transaction.Commit();
            return value;
        }
        catch (GrossDemandItemException ex)
        {
            _session.Transaction.Rollback();
            SetFailure(failedResult, ex.Code, ex.Message);
            return failedResult;
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "{LogMessage}，本项事务已回滚。", logMessage);
            SetFailure(failedResult, "UNEXPECTED_ERROR", logMessage);
            return failedResult;
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task<ServiceResult<T>> ExecuteTransactionAsync<T>(Func<Task<T>> action, string failureMessage)
    {
        if (_session.Transaction is not null) return ServiceResult<T>.Fail("当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            var value = await action();
            _session.Transaction.Commit();
            return ServiceResult<T>.Ok(value);
        }
        catch (GrossDemandValidationException ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<T>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogError(ex, "{FailureMessage}，事务已回滚。", failureMessage);
            return ServiceResult<T>.Fail(failureMessage);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private static string? ValidateCommandItems(IReadOnlyCollection<GrossDemandCommandItem>? items)
    {
        if (items is null || items.Count == 0) return "至少选择一条毛需求。";
        if (items.Any(x => x.Uid == Guid.Empty || x.Version <= 0 || x.ExpectedDecompositionRevision < 0))
            return "批量命令包含无效的 Uid、Version 或拆解版本。";
        if (items.GroupBy(x => x.Uid).Any(x => x.Count() > 1)) return "同一毛需求不能重复提交。";
        return null;
    }

    private static string? ValidateActiveAllocations(
        IReadOnlyList<NetDemand> netDemands,
        IReadOnlyList<NetDemandAllocation> allocations)
    {
        if (netDemands.Count == 0 || allocations.Count != netDemands.Count)
            return "活动净需求与活动计划分配数量不一致，请联系管理员修复数据。";
        var allocationByDemand = allocations.GroupBy(x => x.NetDemandId).ToDictionary(x => x.Key, x => x.ToArray());
        foreach (var demand in netDemands)
        {
            if (!allocationByDemand.TryGetValue(demand.Uid, out var rows) || rows.Length != 1)
                return $"净需求 {demand.SkuSnapshot} 没有且仅有一条活动计划分配。";
            var allocation = rows[0];
            if (allocation.PlanRevision <= 0 || allocation.Sequence <= 0 || allocation.PlannedQty != demand.NetQty)
                return $"净需求 {demand.SkuSnapshot} 的活动计划版本或数量不完整。";
            if (demand.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier)
            {
                if (!demand.DeliveryTargetNetDemandId.HasValue
                    || !allocationByDemand.TryGetValue(demand.DeliveryTargetNetDemandId.Value, out var targetRows)
                    || targetRows.Length != 1
                    || allocation.DeliveryTargetAllocationId != targetRows[0].Uid
                    || allocation.DeliveryTargetSupplierId != targetRows[0].PurchaseSupplierId)
                    return $"客供件净需求 {demand.SkuSnapshot} 的送达目标计划不完整。";
            }
        }
        return null;
    }

    private static void EnsureEditableManual(GrossDemand demand)
    {
        if (!string.Equals(demand.SourceType, DocumentTypeStaticsEnum.ManualDemand, StringComparison.Ordinal))
            throw new GrossDemandValidationException("只有手工来源毛需求可在本工作台编辑或关闭。");
        if (demand.LifecycleStatus != GrossDemandLifecycleStatusEnum.Active
            || demand.DecompositionStatus != GrossDemandDecompositionStatusEnum.Pending)
            throw new GrossDemandValidationException("只有活动且未拆解的手工毛需求可编辑或关闭。");
    }

    private static void EnsureWriteSucceeded(RepositoryWriteResult result)
    {
        if (!result.Success) throw new GrossDemandValidationException(result.Message ?? "数据库写入失败。");
    }

    private static void EnsureItemWriteSucceeded(RepositoryWriteResult result)
    {
        if (!result.Success) throw new GrossDemandItemException(result.ErrorCode ?? "WRITE_FAILED", result.Message);
    }

    private static void SetFailure<T>(T result, string code, string message) where T : class
    {
        switch (result)
        {
            case GrossDemandDecomposeItemResult decompose:
                decompose.Outcome = GrossDemandCommandOutcomeEnum.Failed;
                decompose.ErrorCode = code;
                decompose.Message = message;
                break;
            case GrossDemandRollbackItemResult rollback:
                rollback.Outcome = GrossDemandCommandOutcomeEnum.Failed;
                rollback.ErrorCode = code;
                rollback.Message = message;
                break;
        }
    }

    private sealed class GrossDemandValidationException(string message) : Exception(message);
    private sealed class GrossDemandItemException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
