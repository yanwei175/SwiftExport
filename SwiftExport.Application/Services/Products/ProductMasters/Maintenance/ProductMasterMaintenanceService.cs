using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>编排主型号与中性产品保存，以及按显式选项同步关联物料的校验、并发及请求级事务。</summary>
public sealed partial class ProductMasterMaintenanceService(
    IProductMasterRepository masterRepository,
    IProductNeutralRepository neutralRepository,
    IProductCategoryRepository categoryRepository,
    ISupplierRepository supplierRepository,
    IProductColorRepository colorRepository,
    IProductRepository productRepository,
    ServiceBaseContext context) : IProductMasterMaintenanceService, IProductNeutralMaintenanceService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ProductMasterMaintenanceService> _logger = context.LoggerFactory.CreateLogger<ProductMasterMaintenanceService>();

    /// <summary>在单一事务中批量更新主型号分类；任一无效或并发项都会回滚整个批次。</summary>
    public async Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
        AssignProductMastersToCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.TargetCategoryId == Guid.Empty)
            return ServiceResult<AssignProductMastersToCategoryResult>.Fail(ServiceErrorCodes.Validation, "请选择有效的目标分类。");

        request.Items ??= [];
        if (request.Items.Count == 0)
            return ServiceResult<AssignProductMastersToCategoryResult>.Fail(ServiceErrorCodes.Validation, "请选择需要归类的主型号。");
        if (request.Items.Count > 200)
            return ServiceResult<AssignProductMastersToCategoryResult>.Fail(ServiceErrorCodes.Validation, "单次最多归类 200 个主型号。");
        if (request.Items.Any(item => item is null || item.ProductMasterId == Guid.Empty || item.ExpectedVersion <= 0))
            return ServiceResult<AssignProductMastersToCategoryResult>.Fail(ServiceErrorCodes.Validation, "归类主型号标识或版本无效。");

        var duplicateId = request.Items
            .GroupBy(item => item.ProductMasterId)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateId.HasValue)
            return ServiceResult<AssignProductMastersToCategoryResult>.Fail(ServiceErrorCodes.Validation, $"主型号 {duplicateId.Value} 在本次请求中重复。");

        return await ExecuteTransactionAsync(async () =>
        {
            var categories = await categoryRepository.GetByIdListAsync([request.TargetCategoryId], true);
            if (categories.Count != 1)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "目标分类不存在或已失效。");

            var ids = request.Items.Select(item => item.ProductMasterId).ToArray();
            var masters = await masterRepository.GetByIdListAsync(ids, true);
            if (masters.Count != ids.Length)
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "部分主型号不存在或已被删除，请刷新后重试。");

            var masterMap = masters.ToDictionary(master => master.Uid);
            foreach (var item in request.Items)
            {
                var master = masterMap[item.ProductMasterId];
                if (master.Version != item.ExpectedVersion)
                    throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, $"主型号 {master.MasterSku} 已被其他用户修改，请刷新后重试。");
                if (master.CategoryId == request.TargetCategoryId)
                    throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, $"主型号 {master.MasterSku} 已属于目标分类，请刷新后重试。");
            }

            var response = new AssignProductMastersToCategoryResult { TargetCategoryId = request.TargetCategoryId };
            foreach (var item in request.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await masterRepository.AssignCategoryAsync(
                    item.ProductMasterId,
                    item.ExpectedVersion,
                    request.TargetCategoryId,
                    cancellationToken);
                EnsureWrite(result, "主型号归类失败。");
                response.Items.Add(new AssignedProductMasterVersionResult
                {
                    ProductMasterId = item.ProductMasterId,
                    OldVersion = item.ExpectedVersion,
                    NewVersion = result.NewVersion
                        ?? throw new ProductMasterWriteException(ServiceErrorCodes.Unexpected, "主型号归类成功但未返回新版本号。")
                });
            }

            response.UpdatedCount = response.Items.Count;
            return ServiceResult<AssignProductMastersToCategoryResult>.Ok(response, $"已将 {response.UpdatedCount} 个主型号归入目标分类。");
        }, "批量归类主型号", cancellationToken);
    }

    /// <summary>在单一事务中保存管理页变更，并将新增、修改的主型号资料按选项同步到关联物料。</summary>
    public async Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !request.HasChanges)
            return ServiceResult<ProductMasterBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "没有需要保存的主型号变更。");

        return await ExecuteTransactionAsync(async () =>
        {
            NormalizeBatchRequest(request);
            await ValidateMasterBatchAsync(request, cancellationToken);
            var response = new ProductMasterBatchSaveResponse { TotalCount = request.TotalCount };
            var synchronizationSources = new List<ProductMaster>();

            foreach (var item in request.CreateItems)
            {
                var entity = ToEntity(item.Data);
                var result = await masterRepository.AddAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromCreate(item, result));
                EnsureWrite(result, "新增主型号失败。");
                entity.Uid = result.Uid;
                synchronizationSources.Add(entity);
            }
            foreach (var item in request.UpdateItems)
            {
                var entity = ToEntity(item.Data);
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var result = await masterRepository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, result));
                EnsureWrite(result, "更新主型号失败。");
                synchronizationSources.Add(entity);
            }
            foreach (var item in request.DeleteItems)
            {
                await neutralRepository.SoftDeleteByProductMasterIdAsync(item.Uid, cancellationToken);
                var result = await masterRepository.DeleteAsync(item.Uid, item.Version, true);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, result));
                EnsureWrite(result, "删除主型号失败。");
            }
            // 所有主型号写入通过后才同步；任一同步失败仍由外层事务回滚整个批次。
            foreach (var source in synchronizationSources.OrderBy(source => source.Uid))
                await SynchronizeProductsAsync(source, request.UpdateProductDescriptions, request.UpdateProductDefaultSupplier, cancellationToken);
            return ServiceResult<ProductMasterBatchSaveResponse>.Ok(response, $"已保存 {response.SuccessCount} 条主型号变更。");
        }, "保存主型号列表", cancellationToken);
    }

    /// <summary>在单一事务中保存头部、明细及关联物料同步，任一步失败全部回滚。</summary>
    public async Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default)
    {
        if (request?.Master is null)
            return ServiceResult<SaveProductMasterDetailResponse>.Fail(ServiceErrorCodes.Validation, "主型号详情请求不能为空。");

        return await ExecuteTransactionAsync(async () =>
        {
            NormalizeDetailRequest(request);
            var existingDetail = request.ProductMasterId == Guid.Empty
                ? null
                : await LoadDetailAsync(request.ProductMasterId, true, cancellationToken);
            if (request.ProductMasterId != Guid.Empty && existingDetail is null)
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
            if (existingDetail is not null && existingDetail.Version != request.ExpectedVersion)
                throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号版本已变化，请刷新后重试。");

            await ValidateDetailAsync(request, existingDetail, cancellationToken);
            Guid productMasterId;
            if (existingDetail is null)
            {
                var result = await masterRepository.AddAsync(ToEntity(request.Master));
                EnsureWrite(result, "新增主型号失败。");
                productMasterId = result.Uid;
            }
            else
            {
                var entity = ToEntity(request.Master);
                entity.Uid = request.ProductMasterId;
                entity.Version = request.ExpectedVersion;
                EnsureWrite(await masterRepository.UpdateAsync(entity), "更新主型号失败。");
                productMasterId = request.ProductMasterId;
            }

            await SaveNeutralRowsAsync(productMasterId, NeutralChanges(request));

            var source = ToEntity(request.Master);
            source.Uid = productMasterId;
            await SynchronizeProductsAsync(source, request.UpdateProductDescriptions, request.UpdateProductDefaultSupplier, cancellationToken);

            var saved = await LoadDetailAsync(productMasterId, false, cancellationToken)
                ?? throw new ProductMasterWriteException(ServiceErrorCodes.Unexpected, "保存后无法读取主型号详情。");
            return ServiceResult<SaveProductMasterDetailResponse>.Ok(new SaveProductMasterDetailResponse { Detail = saved }, "主型号详情已保存。");
        }, "保存主型号详情", cancellationToken);
    }


    /// <summary>验证同步供应商并按稳定关联覆盖当前物料资料，不使用客户端供应商显示值。</summary>
    private async Task SynchronizeProductsAsync(ProductMaster source, bool updateDescriptions, bool updateDefaultSupplier, CancellationToken cancellationToken)
    {
        if (updateDefaultSupplier && source.DefaultSupplierId != Guid.Empty)
        {
            // 保持引用校验和资料写入同事务；加锁防止供应商在校验后被停用或删除。
            var supplier = await supplierRepository.GetByIdAsync(source.DefaultSupplierId, true);
            if (supplier is null || supplier.IsDeleted || !supplier.IsActive)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "同步默认供应商失败：供应商不存在、已删除或已停用。");
        }
        cancellationToken.ThrowIfCancellationRequested();
        await productRepository.SyncFromProductMasterAsync(source, updateDescriptions, updateDefaultSupplier, cancellationToken);
    }

    /// <summary>父记录加更新锁后原子保存明细；不回写头部，避免覆盖管理页独立编辑。</summary>
    /// <summary>执行已校验的明细写入；所有仓储共享外层事务。</summary>
    private async Task SaveNeutralRowsAsync(Guid productMasterId, BatchSaveRequestBase<ProductNeutralWriteDto> request)
    {
        foreach (var item in request.CreateItems)
        {
            var entity = ToEntity(item.Data, productMasterId);
            EnsureWrite(await neutralRepository.AddAsync(entity), "新增中性产品失败。");
        }
        foreach (var item in request.UpdateItems)
        {
            var entity = ToEntity(item.Data, productMasterId);
            entity.Uid = item.Uid;
            entity.Version = item.Version;
            EnsureWrite(await neutralRepository.UpdateAsync(entity), "更新中性产品失败。");
        }
        foreach (var item in request.DeleteItems)
            EnsureWrite(await neutralRepository.DeleteAsync(item.Uid, item.Version, true), "删除中性产品失败。");
    }

    /// <summary>复用现有变更包装，不把头部数据带入独立明细保存。</summary>
    private static BatchSaveRequestBase<ProductNeutralWriteDto> NeutralChanges(SaveProductMasterDetailRequest request) => new()
    {
        CreateItems = request.CreateNeutrals,
        UpdateItems = request.UpdateNeutrals,
        DeleteItems = request.DeleteNeutrals
    };

    /// <summary>规范化明细变更并拒绝空项，防止无效请求进入事务写入阶段。</summary>
    private static void NormalizeNeutralChanges(BatchSaveRequestBase<ProductNeutralWriteDto> request)
    {
        request.CreateItems ??= [];
        request.UpdateItems ??= [];
        request.DeleteItems ??= [];
        if (request.CreateItems.Any(item => item?.Data is null)
            || request.UpdateItems.Any(item => item?.Data is null)
            || request.DeleteItems.Any(item => item is null))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "中性产品变更集不能包含空项。");
        foreach (var item in request.CreateItems) Normalize(item.Data);
        foreach (var item in request.UpdateItems) Normalize(item.Data);
    }

    private async Task ValidateMasterBatchAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken)
    {
        var errors = new StringBuilder();
        foreach (var item in request.CreateItems) Append(errors, ValidateMaster(item.Data));
        foreach (var item in request.UpdateItems) Append(errors, ValidateMaster(item.Data));
        if (request.UpdateItems.Any(item => item.Uid == Guid.Empty || item.Version <= 0)
            || request.DeleteItems.Any(item => item.Uid == Guid.Empty || item.Version <= 0))
            Append(errors, "修改或删除的主型号标识及版本必须有效。");
        EnsureNoTargetConflict(request.UpdateItems.Select(item => item.Uid), request.DeleteItems.Select(item => item.Uid));

        var writes = request.CreateItems.Select(item => (Guid?)null).Zip(request.CreateItems.Select(item => item.Data), (id, data) => (Id: id, Data: data))
            .Concat(request.UpdateItems.Select(item => (Id: (Guid?)item.Uid, Data: item.Data))).ToArray();
        EnsureUniqueCodes(writes.Select(item => item.Data.MasterSku), "本次提交存在重复主型号编码。");
        await ValidateMasterReferencesAsync(writes.Select(item => item.Data), cancellationToken);
        var owners = await masterRepository.GetByMasterSkusAsync(writes.Select(item => item.Data.MasterSku), true, cancellationToken);
        if (writes.Any(write => owners.Any(owner => string.Equals(owner.MasterSku, write.Data.MasterSku, StringComparison.OrdinalIgnoreCase) && owner.Uid != write.Id)))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "主型号编码已被其他记录使用。");

        var targets = request.UpdateItems.Select(item => item.Uid).Concat(request.DeleteItems.Select(item => item.Uid)).Distinct().ToArray();
        var existing = await masterRepository.GetByIdListAsync(targets, true);
        if (existing.Count != targets.Length) Append(errors, "修改或删除的主型号不存在或已删除。");
        if (errors.Length > 0) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, errors.ToString().Trim());
    }

    private async Task ValidateDetailAsync(SaveProductMasterDetailRequest request, ProductMasterDetailDto? existingDetail, CancellationToken cancellationToken)
    {
        var masterError = ValidateMaster(request.Master);
        if (masterError is not null) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, masterError);
        await ValidateMasterReferencesAsync([request.Master], cancellationToken);

        var masterOwners = await masterRepository.GetByMasterSkusAsync([request.Master.MasterSku], true, cancellationToken);
        if (masterOwners.Any(owner => owner.Uid != request.ProductMasterId))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "主型号编码已被其他记录使用。");
        await ValidateNeutralChangesAsync(NeutralChanges(request), existingDetail, cancellationToken);
    }

    /// <summary>统一校验聚合保存与独立保存的明细归属、版本、引用及最终主图状态。</summary>
    private async Task ValidateNeutralChangesAsync(BatchSaveRequestBase<ProductNeutralWriteDto> request, ProductMasterDetailDto? existingDetail, CancellationToken cancellationToken)
    {
        var neutralWrites = request.CreateItems.Select(item => (Guid?)null).Zip(request.CreateItems.Select(item => item.Data), (id, data) => (Id: id, Data: data))
            .Concat(request.UpdateItems.Select(item => (Id: (Guid?)item.Uid, Data: item.Data))).ToArray();
        foreach (var write in neutralWrites)
        {
            var error = ValidateNeutral(write.Data);
            if (error is not null) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, error);
        }
        EnsureUniqueCodes(neutralWrites.Select(item => item.Data.ProductCode), "本次提交存在重复中性产品编码。");
        EnsureNoTargetConflict(request.UpdateItems.Select(item => item.Uid), request.DeleteItems.Select(item => item.Uid));

        var currentIds = existingDetail?.Neutrals.Select(item => item.Uid).ToHashSet() ?? [];
        var targetIds = request.UpdateItems.Select(item => item.Uid).Concat(request.DeleteItems.Select(item => item.Uid)).ToArray();
        if (targetIds.Any(id => id == Guid.Empty || !currentIds.Contains(id)))
            throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "中性产品不得跨主型号更新或删除。");
        foreach (var item in request.UpdateItems)
        {
            var current = existingDetail!.Neutrals.Single(row => row.Uid == item.Uid);
            if (current.Version != item.Version) throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");
        }
        foreach (var item in request.DeleteItems)
        {
            var current = existingDetail!.Neutrals.Single(row => row.Uid == item.Uid);
            if (current.Version != item.Version) throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");
        }
        if (ProductMasterMainImagePolicy.HasConflict(existingDetail, request))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "同一个主型号只能设置一个默认主图。");

        var neutralOwners = await neutralRepository.GetByProductCodesAsync(neutralWrites.Select(item => item.Data.ProductCode), true, cancellationToken);
        if (neutralWrites.Any(write => neutralOwners.Any(owner => string.Equals(owner.ProductCode, write.Data.ProductCode, StringComparison.OrdinalIgnoreCase) && owner.Uid != write.Id)))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "中性产品编码已被其他记录使用。");

        var colorIds = neutralWrites.Select(item => item.Data.ColorId).Distinct().ToArray();
        var colors = await colorRepository.GetByIdListAsync(colorIds, true);
        if (colors.Count != colorIds.Length)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "颜色不存在或已删除。");
    }

    private async Task ValidateMasterReferencesAsync(IEnumerable<ProductMasterWriteDto> masters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = masters.ToArray();
        // 空 Guid 表示当前未设置可选关系；只有用户实际选择后才校验引用有效性。
        var categoryIds = values.Select(item => item.CategoryId).Where(id => id != Guid.Empty).Distinct().ToArray();
        var supplierIds = values.Select(item => item.DefaultSupplierId).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (categoryIds.Length > 0)
        {
            var categories = await categoryRepository.GetByIdListAsync(categoryIds, true);
            if (categories.Count != categoryIds.Length)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "产品分类不存在或已删除。");
        }
        if (supplierIds.Length > 0)
        {
            var suppliers = await supplierRepository.GetByIdListAsync(supplierIds, true);
            if (suppliers.Count != supplierIds.Length)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "默认供应商不存在或已删除。");
        }
    }

    private async Task<ProductMasterDetailDto?> LoadDetailAsync(Guid productMasterId, bool withUpdLock, CancellationToken cancellationToken)
    {
        var detail = await masterRepository.GetDetailHeaderAsync(productMasterId, withUpdLock, cancellationToken);
        if (detail is null) return null;
        detail.Neutrals = (await neutralRepository.GetByProductMasterIdAsync(productMasterId, withUpdLock, cancellationToken)).ToList();
        return detail;
    }

    private async Task<ServiceResult<T>> ExecuteTransactionAsync<T>(Func<Task<ServiceResult<T>>> action, string operation, CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null)
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已存在未完成事务。");
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var result = await action();
            _session.Transaction.Commit();
            return result;
        }
        catch (OperationCanceledException)
        {
            TryRollback();
            throw;
        }
        catch (ProductMasterWriteException exception)
        {
            TryRollback();
            _logger.LogWarning(exception, "{Operation}被业务校验拒绝，事务已回滚。", operation);
            return ServiceResult<T>.Fail(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            TryRollback();
            _logger.LogError(exception, "{Operation}发生未预期异常，事务已回滚。", operation);
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, $"{operation}失败，所有操作已回滚。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    private void TryRollback()
    {
        try { _session.Transaction?.Rollback(); }
        catch (Exception exception) { _logger.LogError(exception, "回滚主产品事务失败。"); }
    }

    private static void EnsureWrite(RepositoryWriteResult result, string message)
    {
        if (result.Success) return;
        var code = string.Equals(result.ErrorCode, "CONCURRENCY_CONFLICT", StringComparison.OrdinalIgnoreCase)
            ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.BusinessBlocked;
        throw new ProductMasterWriteException(code, string.IsNullOrWhiteSpace(result.Message) ? message : result.Message);
    }

    private static string? ValidateMaster(ProductMasterWriteDto data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.MasterSku)) return "主型号编码不能为空。";
        return null;
    }

    private static string? ValidateNeutral(ProductNeutralWriteDto data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.ProductCode)) return "中性产品编码不能为空。";
        if (data.ColorId == Guid.Empty) return "中性产品颜色不能为空。";
        if (data.DefaultSalesPriceUsd < 0 || data.DefaultPurchasePriceCny < 0) return "中性产品价格不能小于零。";
        if (data.DefaultExchangeRate.HasValue && data.DefaultExchangeRate <= 0) return "中性产品汇率必须大于零。";
        return null;
    }

    private static ProductMaster ToEntity(ProductMasterWriteDto data) => new()
    {
        MasterSku = data.MasterSku.Trim(), DefaultDescriptionEn = Normalize(data.DefaultDescriptionEn),
        DefaultDescriptionZh = Normalize(data.DefaultDescriptionZh), DefaultFeaturesEn = Normalize(data.DefaultFeaturesEn),
        DefaultFeaturesZh = Normalize(data.DefaultFeaturesZh), DefaultPcsPerPackage = data.DefaultPcsPerPackage,
        DefaultNetWeight = data.DefaultNetWeight, DefaultGrossWeight = data.DefaultGrossWeight,
        DefaultCbm = data.DefaultCbm, ProductType = Normalize(data.ProductType), ProductTag = Normalize(data.ProductTag),
        CategoryId = data.CategoryId, DefaultSupplierId = data.DefaultSupplierId
    };

    private static ProductNeutral ToEntity(ProductNeutralWriteDto data, Guid productMasterId) => new()
    {
        ProductMasterId = productMasterId, ProductCode = data.ProductCode.Trim(), ColorId = data.ColorId,
        DefaultSalesPriceUsd = data.DefaultSalesPriceUsd, DefaultExchangeRate = data.DefaultExchangeRate,
        ProfitMargin = data.ProfitMargin, DefaultPurchasePriceCny = data.DefaultPurchasePriceCny,
        ImagePath = Normalize(data.ImagePath), IsMainImage = data.IsMainImage
    };

    private static void NormalizeBatchRequest(ProductMasterBatchSaveRequest request)
    {
        request.CreateItems ??= [];
        request.UpdateItems ??= [];
        request.DeleteItems ??= [];
        if (request.CreateItems.Any(item => item?.Data is null)
            || request.UpdateItems.Any(item => item?.Data is null)
            || request.DeleteItems.Any(item => item is null))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "主型号变更集不能包含空项。");
        foreach (var item in request.CreateItems) Normalize(item.Data);
        foreach (var item in request.UpdateItems) Normalize(item.Data);
    }

    private static void NormalizeDetailRequest(SaveProductMasterDetailRequest request)
    {
        request.CreateNeutrals ??= [];
        request.UpdateNeutrals ??= [];
        request.DeleteNeutrals ??= [];
        if (request.CreateNeutrals.Any(item => item?.Data is null)
            || request.UpdateNeutrals.Any(item => item?.Data is null)
            || request.DeleteNeutrals.Any(item => item is null))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "中性产品变更集不能包含空项。");
        Normalize(request.Master);
        foreach (var item in request.CreateNeutrals) Normalize(item.Data);
        foreach (var item in request.UpdateNeutrals) Normalize(item.Data);
    }

    private static void Normalize(ProductMasterWriteDto? data)
    {
        if (data is null) return;
        data.MasterSku = data.MasterSku?.Trim();
    }

    private static void Normalize(ProductNeutralWriteDto? data)
    {
        if (data is null) return;
        data.ProductCode = data.ProductCode?.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUniqueCodes(IEnumerable<string> values, string message)
    {
        var codes = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        if (codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != codes.Length)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, message);
    }

    private static void EnsureNoTargetConflict(IEnumerable<Guid> updateIds, IEnumerable<Guid> deleteIds)
    {
        var updates = updateIds.ToArray();
        var deletes = deleteIds.ToArray();
        if (updates.Distinct().Count() != updates.Length || deletes.Distinct().Count() != deletes.Length || updates.Intersect(deletes).Any())
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "同一记录不能重复提交或同时修改和删除。");
    }

    private static void Append(StringBuilder errors, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) errors.AppendLine(error);
    }

    private sealed class ProductMasterWriteException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
