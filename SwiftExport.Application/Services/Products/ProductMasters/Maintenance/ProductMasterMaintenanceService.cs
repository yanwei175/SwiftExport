using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.DTOs.Products;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Data;
using System.Text;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>编排主型号、中性产品和标签选择保存，以及按显式选项同步关联物料的校验、并发及请求级事务。</summary>
public sealed partial class ProductMasterMaintenanceService(
    IProductMasterRepository masterRepository,
    IProductNeutralRepository neutralRepository,
    IProductCategoryRepository categoryRepository,
    ISupplierRepository supplierRepository,
    IProductColorRepository colorRepository,
    IProductRepository productRepository,
    IProductAttributeRepository attributeRepository,
    ServiceBaseContext context) : IProductMasterMaintenanceService, IProductNeutralMaintenanceService
{
    private readonly IDbSession _session = context.Session;
    private readonly ILogger<ProductMasterMaintenanceService> _logger = context.LoggerFactory.CreateLogger<ProductMasterMaintenanceService>();

    /// <summary>锁定所选产品及其有效主型号，在同一事务内拉取指定资料组；无可用主型号的产品跳过。</summary>
    public Task<ServiceResult<PullProductMasterDataResponse>> PullProductsAsync(
        PullProductMasterDataRequest request,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default)
    {
        if (request?.ProductIds is null || request.ProductIds.Count == 0 || request.ProductIds.Count > 200
            || request.ProductIds.Any(id => id == Guid.Empty))
            return Task.FromResult(ServiceResult<PullProductMasterDataResponse>.Fail(ServiceErrorCodes.Validation, "请选择 1 至 200 个有效产品。"));
        if (fields == ProductMasterSyncFields.None || (fields & ~ProductMasterSyncFields.All) != 0)
            return Task.FromResult(ServiceResult<PullProductMasterDataResponse>.Fail(ServiceErrorCodes.Validation, "拉取资料组无效。"));

        var ids = request.ProductIds.Distinct().OrderBy(id => id).ToArray();
        return ExecuteTransactionAsync(async () =>
        {
            // 先只读取得关联，再按“主型号→产品”的既有下发锁顺序加锁，避免两个入口互相等待。
            var observedProducts = await productRepository.GetByIdListAsync(ids);
            if (observedProducts.Count != ids.Length || observedProducts.Any(product => product.IsDeleted))
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "所选产品不存在或已删除，请刷新后重试。");
            var observedMasterIds = observedProducts.ToDictionary(product => product.Uid, product => product.ProductMasterId);

            var masterIds = observedProducts.Where(product => product.ProductMasterId.HasValue && product.ProductMasterId != Guid.Empty)
                .Select(product => product.ProductMasterId!.Value).Distinct().OrderBy(id => id).ToArray();
            IReadOnlyList<ProductMaster> masters = masterIds.Length == 0
                ? []
                : await masterRepository.GetByIdListAsync(masterIds, true);
            var products = await productRepository.GetByIdListAsync(ids, true);
            if (products.Count != ids.Length || products.Any(product => product.IsDeleted)
                || products.Any(product => observedMasterIds[product.Uid] != product.ProductMasterId))
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "所选产品的主型号关联已变化，请刷新后重试。");
            var validMasters = masters.Where(master => !master.IsDeleted).ToDictionary(master => master.Uid);
            var eligible = products.Where(product => product.ProductMasterId.HasValue
                && validMasters.ContainsKey(product.ProductMasterId.Value)).ToArray();

            if ((fields & ProductMasterSyncFields.Supplier) != 0)
            {
                // 只校验真正参与拉取的来源；无关联或来源失效的产品不会写入供应商。
                var supplierIds = eligible.Select(product => validMasters[product.ProductMasterId!.Value].DefaultSupplierId)
                    .Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
                var suppliers = await supplierRepository.GetByIdListAsync(supplierIds, true);
                if (suppliers.Count != supplierIds.Length || suppliers.Any(supplier => supplier.IsDeleted || !supplier.IsActive))
                    throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "拉取默认供应商失败：部分供应商不存在、已删除或已停用。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var updated = eligible.Length == 0 ? 0 : await productRepository.PullSelectedFieldsFromMastersAsync(
                eligible.Select(product => product.Uid).ToArray(), fields, cancellationToken);
            return ServiceResult<PullProductMasterDataResponse>.Ok(new PullProductMasterDataResponse
            {
                SelectedProductCount = ids.Length,
                SkippedProductCount = ids.Length - eligible.Length,
                UpdatedProductCount = updated
            }, $"已检查 {ids.Length} 个产品，跳过 {ids.Length - eligible.Length} 个，更新 {updated} 个。");
        }, "从主型号拉取产品资料", cancellationToken);
    }

    /// <summary>在单一事务中锁定并校验所选主型号，再从数据库当前值向关联产品下发指定资料组。</summary>
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncProductsAsync(
        SyncProductMasterDataRequest request,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default)
    {
        if (request?.ProductMasterIds is null || request.ProductMasterIds.Count == 0 || request.ProductMasterIds.Count > 200
            || request.ProductMasterIds.Any(id => id == Guid.Empty))
            return Task.FromResult(ServiceResult<SyncProductMasterDataResponse>.Fail(ServiceErrorCodes.Validation, "请选择 1 至 200 个有效主型号。"));
        if (fields == ProductMasterSyncFields.None || (fields & ~ProductMasterSyncFields.All) != 0)
            return Task.FromResult(ServiceResult<SyncProductMasterDataResponse>.Fail(ServiceErrorCodes.Validation, "下发资料组无效。"));

        var ids = request.ProductMasterIds.Distinct().OrderBy(id => id).ToArray();
        return ExecuteTransactionAsync(async () =>
        {
            var masters = await masterRepository.GetByIdListAsync(ids, true);
            if (masters.Count != ids.Length || masters.Any(master => master.IsDeleted))
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "所选主型号不存在或已删除，请刷新后重试。");
            if ((fields & ProductMasterSyncFields.Supplier) != 0)
            {
                // 空 Guid 明确清空产品默认供应商；非空值必须在同一事务内锁定并校验活动资格。
                var supplierIds = masters.Select(master => master.DefaultSupplierId).Where(id => id != Guid.Empty).Distinct().ToArray();
                var suppliers = await supplierRepository.GetByIdListAsync(supplierIds, true);
                if (suppliers.Count != supplierIds.Length || suppliers.Any(supplier => supplier.IsDeleted || !supplier.IsActive))
                    throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "下发默认供应商失败：部分供应商不存在、已删除或已停用。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var changed = await productRepository.SyncSelectedFieldsFromMastersAsync(ids, fields, cancellationToken);
            return ServiceResult<SyncProductMasterDataResponse>.Ok(new SyncProductMasterDataResponse
            {
                ProductMasterCount = ids.Length,
                UpdatedProductCount = changed
            }, $"已下发 {ids.Length} 个主型号，更新 {changed} 个关联产品。");
        }, "独立下发主型号资料", cancellationToken);
    }

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
            var ids = request.Items.Select(item => item.ProductMasterId).OrderBy(id => id).ToArray();
            var observed = await ObserveAndLockCategoriesAsync(ids, [request.TargetCategoryId], cancellationToken);
            var masters = await LockMastersAsync(ids, cancellationToken);
            if (masters.Count != ids.Length)
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "部分主型号不存在或已被删除，请刷新后重试。");

            var masterMap = masters.ToDictionary(master => master.Uid);
            if (masters.Any(row => !observed.TryGetValue(row.Uid, out var categoryId) || categoryId != row.CategoryId))
                throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号分类或属性模板已变化，请刷新后重试。");
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

    /// <summary>在单一事务中校验并保存管理页的新增、修改、删除，再按请求选项同步新增及修改项关联的物料。</summary>
    /// <remarks>同步选项不作用于删除项；任一主型号或物料写入失败均回滚整个批次。</remarks>
    public async Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.ContractVersion != 3 || !request.HasChanges)
            return ServiceResult<ProductMasterBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "主型号保存契约版本无效或没有需要保存的变更。");

        return await ExecuteTransactionAsync(async () =>
        {
            NormalizeBatchRequest(request);
            await ValidateMasterBatchAsync(request, cancellationToken);
            var response = new ProductMasterBatchSaveResponse { TotalCount = request.TotalCount };
            // 只收集新增和修改的主型号；删除项仅处理主型号及其中性产品，不向物料写入同步值。
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
                await RemoveAttributeSelectionsAsync(item.Uid, cancellationToken);
                await neutralRepository.SoftDeleteByProductMasterIdAsync(item.Uid, cancellationToken);
                var result = await masterRepository.DeleteAsync(item.Uid, item.Version, true);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, result));
                EnsureWrite(result, "删除主型号失败。");
            }
            // 所有主型号写入通过后，按 UID 顺序同步；请求的两个开关对本批次每个同步源使用同一取值。
            // 任一同步失败仍由外层事务回滚整个批次。
            foreach (var source in synchronizationSources.OrderBy(source => source.Uid))
                await SynchronizeProductsAsync(source, request.UpdateProductDescriptions, request.UpdateProductDefaultSupplier, cancellationToken);
            return ServiceResult<ProductMasterBatchSaveResponse>.Ok(response, $"已保存 {response.SuccessCount} 条主型号变更。");
        }, "保存主型号列表", cancellationToken);
    }

    /// <summary>在单一事务中保存头部、标签最终选择、明细及关联物料同步，任一步失败全部回滚。</summary>
    public async Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default)
    {
        if (request?.Master is null || request.ContractVersion != 3)
            return ServiceResult<SaveProductMasterDetailResponse>.Fail(ServiceErrorCodes.Validation, "主型号详情请求为空或契约版本无效。");

        return await ExecuteTransactionAsync(async () =>
        {
            NormalizeDetailRequest(request);
            // 与分类删除及颜色删除保持父资料先于主型号/明细，锁后仍重验目标版本。
            var masterError = ValidateMaster(request.Master);
            if (masterError is not null) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, masterError);
            var observed = await ObserveAndLockCategoriesAsync(request.ProductMasterId == Guid.Empty ? [] : [request.ProductMasterId],
                [request.Master.CategoryId], cancellationToken);
            var observedReferences = await ObserveAndLockDictionaryReferencesAsync(request.ProductMasterId == Guid.Empty ? [] : [request.ProductMasterId],
                [request.Master.ProductAttributeProfileId], [request.Master.ProductSeriesId], cancellationToken);
            await LockAttributeGroupsAsync(request.ProductMasterId == Guid.Empty ? [] : [request.ProductMasterId], request.SelectedOptionIds, cancellationToken);
            await ValidateMasterReferencesAsync([request.Master], cancellationToken);
            await LockNeutralColorsAsync(NeutralChanges(request), cancellationToken);
            var existingDetail = request.ProductMasterId == Guid.Empty
                ? null
                : await LoadDetailAsync(request.ProductMasterId, true, cancellationToken);
            if (request.ProductMasterId != Guid.Empty && existingDetail is null)
                throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
            if (existingDetail is not null && existingDetail.Version != request.ExpectedVersion)
                throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号版本已变化，请刷新后重试。");
            if (existingDetail is not null && (!observed.TryGetValue(existingDetail.Uid, out var oldCategory)
                || oldCategory != existingDetail.Master.CategoryId
                || !observedReferences.TryGetValue(existingDetail.Uid, out var oldReferences)
                || oldReferences.ProfileId != existingDetail.Master.ProductAttributeProfileId
                || oldReferences.SeriesId != existingDetail.Master.ProductSeriesId))
                throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号分类、属性模板或系列已变化，请刷新后重试。");

            await ValidateDetailAsync(request, existingDetail, cancellationToken);
            var selectedIds = request.SelectedOptionIds ?? existingDetail?.SelectedOptionIds ?? [];
            await ValidateAttributeSelectionsAsync(request.Master.ProductAttributeProfileId, selectedIds, cancellationToken);
            if (existingDetail is not null && existingDetail.Master.ProductAttributeProfileId != request.Master.ProductAttributeProfileId)
                await ValidateLinkedProductSelectionsAsync(request.ProductMasterId, request.Master.ProductAttributeProfileId, false, cancellationToken);
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

            await SaveAttributeSelectionsAsync(productMasterId, selectedIds, cancellationToken);
            await SaveNeutralRowsAsync(productMasterId, NeutralChanges(request));

            var source = ToEntity(request.Master);
            source.Uid = productMasterId;
            await SynchronizeProductsAsync(source, request.UpdateProductDescriptions, request.UpdateProductDefaultSupplier, cancellationToken);

            var saved = await LoadDetailAsync(productMasterId, false, cancellationToken)
                ?? throw new ProductMasterWriteException(ServiceErrorCodes.Unexpected, "保存后无法读取主型号详情。");
            return ServiceResult<SaveProductMasterDetailResponse>.Ok(new SaveProductMasterDetailResponse { Detail = saved }, "主型号详情已保存。");
        }, "保存主型号详情", cancellationToken);
    }


    /// <summary>同步前校验所选默认供应商为有效且启用的记录，再委托仓储按主型号 UID 更新关联物料。</summary>
    /// <remarks>主型号编码始终参与同步；两个布尔值分别控制四个描述/配置字段和供应商 ID，空供应商表示清除。</remarks>
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

    /// <summary>校验最终批次并先锁父资料、模板和标签组；列表模板变更必须保留全部已有选择。</summary>
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
        var targets = request.UpdateItems.Select(item => item.Uid).Concat(request.DeleteItems.Select(item => item.Uid)).Distinct().OrderBy(id => id).ToArray();
        var observed = await ObserveAndLockCategoriesAsync(targets, writes.Select(item => item.Data.CategoryId), cancellationToken);
        var observedReferences = await ObserveAndLockDictionaryReferencesAsync(targets, writes.Select(item => item.Data.ProductAttributeProfileId),
            writes.Select(item => item.Data.ProductSeriesId), cancellationToken);
        await LockAttributeGroupsAsync(targets, null, cancellationToken);
        await ValidateMasterReferencesAsync(writes.Select(item => item.Data), cancellationToken);
        var existing = await LockMastersAsync(targets, cancellationToken);
        // 编码唯一性由数据库约束兜底；不先锁编码拥有者，以免与修改/删除身份集合形成逆序锁。
        var owners = await masterRepository.GetByMasterSkusAsync(writes.Select(item => item.Data.MasterSku), false, cancellationToken);
        if (writes.Any(write => owners.Any(owner => string.Equals(owner.MasterSku, write.Data.MasterSku, StringComparison.OrdinalIgnoreCase) && owner.Uid != write.Id)))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "主型号编码已被其他记录使用。");

        if (existing.Count != targets.Length) Append(errors, "修改或删除的主型号不存在或已删除。");
        if (errors.Length > 0) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, errors.ToString().Trim());
        EnsureObservedReferences(observed, observedReferences, existing);
        var versions = request.UpdateItems.Select(item => (item.Uid, item.Version)).Concat(request.DeleteItems.Select(item => (item.Uid, item.Version))).ToDictionary(item => item.Uid, item => item.Version);
        if (existing.Any(item => item.Version != versions[item.Uid]))
            throw new ProductMasterWriteException(ServiceErrorCodes.VersionConflict, "主型号版本已变化，请刷新后重试。");
        foreach (var item in request.UpdateItems)
        {
            var original = existing.Single(row => row.Uid == item.Uid);
            await ValidateRetainedSelectionsAsync(original, item.Data.ProductAttributeProfileId, cancellationToken);
            if (original.ProductAttributeProfileId != item.Data.ProductAttributeProfileId)
                await ValidateLinkedProductSelectionsAsync(item.Uid, item.Data.ProductAttributeProfileId, false, cancellationToken);
        }
        foreach (var item in request.DeleteItems)
            await ValidateLinkedProductSelectionsAsync(item.Uid, null, true, cancellationToken);
    }

    /// <summary>父资料已先锁定后校验编码和明细状态，避免持有主从锁后反向锁父资料。</summary>
    private async Task ValidateDetailAsync(SaveProductMasterDetailRequest request, ProductMasterDetailDto? existingDetail, CancellationToken cancellationToken)
    {
        var masterError = ValidateMaster(request.Master);
        if (masterError is not null) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, masterError);
        // 目标主型号已按身份持锁；编码占用者只读，防止两个详情互换编码时逆序锁定对方。
        // 编码唯一约束仍是写入竞争的最终防线。
        var masterOwners = await masterRepository.GetByMasterSkusAsync([request.Master.MasterSku], false, cancellationToken);
        if (masterOwners.Any(owner => owner.Uid != request.ProductMasterId))
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "主型号编码已被其他记录使用。");
        await ValidateNeutralChangesAsync(NeutralChanges(request), existingDetail, cancellationToken);
    }

    /// <summary>在颜色引用已验证后，统一校验聚合与独立保存的明细归属、版本、编码及最终主图状态。</summary>
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

    }

    /// <summary>只锁本次新增/更新引用的颜色，必须先于主型号和中性行；删除行不新增引用，无需锁其旧颜色。</summary>
    private async Task LockNeutralColorsAsync(BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writes = request.CreateItems.Select(item => item.Data).Concat(request.UpdateItems.Select(item => item.Data)).ToArray();
        foreach (var write in writes)
        {
            var error = ValidateNeutral(write);
            if (error is not null) throw new ProductMasterWriteException(ServiceErrorCodes.Validation, error);
        }
        var colorIds = writes.Select(item => item.ColorId).Distinct().OrderBy(id => id).ToArray();
        if (colorIds.Length == 0) return;
        var colors = await colorRepository.GetByIdListAsync(colorIds, true);
        if (colors.Count != colorIds.Length)
            throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "颜色不存在或已删除。");
    }

    /// <summary>分类已由锁前观察统一校验并锁定后，再校验供应商；保持父资料先于主型号的锁顺序。</summary>
    private async Task ValidateMasterReferencesAsync(IEnumerable<ProductMasterWriteDto> masters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = masters.ToArray();
        // 空 Guid 表示当前未设置可选关系；只有用户实际选择后才校验引用有效性。
        var supplierIds = values.Select(item => item.DefaultSupplierId).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (supplierIds.Length > 0)
        {
            var suppliers = await supplierRepository.GetByIdListAsync(supplierIds, true);
            if (suppliers.Count != supplierIds.Length)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "默认供应商不存在或已删除。");
        }
    }

    /// <summary>读取主型号聚合及当前选择；失效选项身份仍返回，不能隐式解释为清空。</summary>
    private async Task<ProductMasterDetailDto?> LoadDetailAsync(Guid productMasterId, bool withUpdLock, CancellationToken cancellationToken)
    {
        var detail = await masterRepository.GetDetailHeaderAsync(productMasterId, withUpdLock, cancellationToken);
        if (detail is null) return null;
        detail.Neutrals = (await neutralRepository.GetByProductMasterIdAsync(productMasterId, withUpdLock, cancellationToken)).ToList();
        detail.AttributeSelections = (await attributeRepository.GetSelectionDetailsAsync(ProductAttributeOwnerType.ProductMaster, [productMasterId], cancellationToken)).ToList();
        detail.SelectedOptionIds = detail.AttributeSelections.Select(row => row.OptionId).ToList();
        return detail;
    }

    /// <summary>为主型号保存链路提供单一数据库事务；业务失败和异常均回滚，并清理当前会话事务。</summary>
    private async Task<ServiceResult<T>> ExecuteTransactionAsync<T>(Func<Task<ServiceResult<T>>> action, string operation, CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null)
            return ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已存在未完成事务。");
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>保留原写入失败边界，仅将仓储明确识别的编码占用映射为输入校验；组合占用仍按业务阻断处理。</summary>
    private static void EnsureWrite(RepositoryWriteResult result, string message)
    {
        if (result.Success) return;
        var code = result.ErrorCode?.ToUpperInvariant() switch
        {
            ServiceErrorCodes.ConcurrencyConflict => ServiceErrorCodes.VersionConflict,
            ProductErrorCodes.ProductMasterSkuOccupied or ProductErrorCodes.ProductNeutralCodeOccupied => ServiceErrorCodes.Validation,
            ProductErrorCodes.ProductNeutralMasterColorOccupied => ServiceErrorCodes.BusinessBlocked,
            _ => ServiceErrorCodes.BusinessBlocked
        };
        throw new ProductMasterWriteException(code, string.IsNullOrWhiteSpace(result.Message) ? message : result.Message);
    }

    /// <summary>校验普通列表与详情共用的主型号输入；默认装率可以未设置，有值必须为正整数。</summary>
    private static string? ValidateMaster(ProductMasterWriteDto data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.MasterSku)) return "主型号编码不能为空。";
        if (data.DefaultPcsPerPackage is <= 0) return "默认每箱装率有值时必须为正整数。";
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

    /// <summary>构造主型号可写字段，系列与模板按字典身份存储，不将展示名称写回实体。</summary>
    private static ProductMaster ToEntity(ProductMasterWriteDto data) => new()
    {
        MasterSku = data.MasterSku.Trim(),
        DefaultDescriptionEn = Normalize(data.DefaultDescriptionEn),
        DefaultDescriptionZh = Normalize(data.DefaultDescriptionZh),
        DefaultFeaturesEn = Normalize(data.DefaultFeaturesEn),
        DefaultFeaturesZh = Normalize(data.DefaultFeaturesZh),
        DefaultPcsPerPackage = data.DefaultPcsPerPackage,
        DefaultNetWeight = data.DefaultNetWeight,
        DefaultGrossWeight = data.DefaultGrossWeight,
        DefaultCbm = data.DefaultCbm,
        ProductAttributeProfileId = data.ProductAttributeProfileId,
        ProductSeriesId = data.ProductSeriesId,
        CategoryId = data.CategoryId,
        DefaultSupplierId = data.DefaultSupplierId
    };

    private static ProductNeutral ToEntity(ProductNeutralWriteDto data, Guid productMasterId) => new()
    {
        ProductMasterId = productMasterId,
        ProductCode = data.ProductCode.Trim(),
        ColorId = data.ColorId,
        DefaultSalesPriceUsd = data.DefaultSalesPriceUsd,
        DefaultExchangeRate = data.DefaultExchangeRate,
        ProfitMargin = data.ProfitMargin,
        DefaultPurchasePriceCny = data.DefaultPurchasePriceCny,
        ImagePath = Normalize(data.ImagePath),
        IsMainImage = data.IsMainImage
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
