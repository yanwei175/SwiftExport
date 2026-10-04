using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;
using SwiftExport.Application.Services.Products.Catalog.DefaultSupplier;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;
namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>
/// 负责产品资料写入、批量保存和主数据引用校验；查询由产品查询服务提供。
/// </summary>
public partial class ProductMaintenanceService(IProductRepository productRepository, ServiceBaseContext context,
    IProductColorRepository colorRepository,
    ISupplierRepository supplierRepository,
    IWarehouseQueryRepository warehouseRepository,
    IBomRepository bomRepository,
    ISysDictDataRepository dictDataRepository)
    : BaseService<Product, ProductDto>(productRepository, context), IProductMaintenanceService
{
    private readonly ILogger<ProductMaintenanceService> _logger = context.LoggerFactory.CreateLogger<ProductMaintenanceService>();

    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<ProductDto>> createItems)
    {
        foreach (var item in createItems)
        {
            // Product.CategoryId 仅保留旧库兼容占位，新增物料统一写入空标识。
            item.Data.CategoryId = Guid.Empty;
            var error = ValidateProduct(item.Data, item.RowIndex, "新增");
            if (error is not null) return error;
        }

        var duplicateSku = FindDuplicateSku(createItems.Select(item => item.Data.Sku));
        if (duplicateSku is not null)
            return $"新增失败：本次提交中 SKU {duplicateSku} 重复。";

        var skus = createItems.Select(item => item.Data.Sku).ToArray();
        var existingProducts = await productRepository.GetBySkuListAsync(skus);
        if (existingProducts.Count > 0)
            return "新增失败：以下 SKU 已存在：" + string.Join(", ", existingProducts.Select(product => product.Sku));

        var referenceError = await ValidateMasterDataReferencesAsync(
            createItems.Select(item => (item.Data, (Product?)null)));
        if (referenceError is not null) return referenceError;

        var unitError = await ValidateAndNormalizeUnitsAsync(
            createItems.Select(item => new ProductUnitValidationItem(item.Data, null, item.RowIndex)));
        if (unitError is not null) return unitError;

        foreach (var item in createItems)
        {
            var error = await ValidateCustomerSuppliedAsync(item.Data, Guid.Empty);
            if (error is not null) return error;
        }

        return null;
    }

    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<ProductDto>> updateItems)
    {
        foreach (var item in updateItems)
        {
            // 分类是 ProductMaster 的只读投影，禁止随普通产品保存回写旧字段。
            item.Data.CategoryId = Guid.Empty;
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return $"第 {FormatRowIndex(item.RowIndex)}：产品标识或版本无效。";

            var error = ValidateProduct(item.Data, item.RowIndex, "修改");
            if (error is not null) return error;
        }

        var duplicateUid = FindDuplicateUid(updateItems.Select(item => item.Uid));
        if (duplicateUid.HasValue)
            return $"修改失败：产品 {duplicateUid.Value} 在本次提交中重复。";

        var duplicateSku = FindDuplicateSku(updateItems.Select(item => item.Data.Sku));
        if (duplicateSku is not null)
            return $"修改失败：本次提交中 SKU {duplicateSku} 重复。";

        var ids = updateItems.Select(item => item.Uid).ToArray();
        var existingProductsById = await _repository.GetByIdListAsync(ids, withUpdLock: true);
        var missingIds = ids.Except(existingProductsById.Select(product => product.Uid)).ToArray();
        if (missingIds.Length > 0)
            return "修改失败：包含不存在或已删除的产品：" + string.Join(", ", missingIds);
        var existingById = existingProductsById.ToDictionary(product => product.Uid);

        var existingProductsBySku = await productRepository.GetBySkuListAsync(
            updateItems.Select(item => item.Data.Sku));
        var occupiedSkus = existingProductsBySku
            .Where(existing => updateItems.Any(item =>
                item.Uid != existing.Uid
                && string.Equals(item.Data.Sku, existing.Sku, StringComparison.OrdinalIgnoreCase)))
            .Select(product => product.Sku)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (occupiedSkus.Length > 0)
            return "修改失败：以下 SKU 已被其他产品使用：" + string.Join(", ", occupiedSkus);

        var referenceError = await ValidateMasterDataReferencesAsync(
            updateItems.Select(item => (item.Data, (Product?)existingById[item.Uid])));
        if (referenceError is not null) return referenceError;

        var unitError = await ValidateAndNormalizeUnitsAsync(updateItems.Select(item =>
            new ProductUnitValidationItem(item.Data, existingById[item.Uid].Unit, item.RowIndex)));
        if (unitError is not null) return unitError;

        foreach (var item in updateItems)
        {
            var error = await ValidateCustomerSuppliedAsync(item.Data, item.Uid);
            if (error is not null) return error;
        }

        return null;
    }

    protected override async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> deleteItems)
    {
        foreach (var item in deleteItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                return $"第 {FormatRowIndex(item.RowIndex)}：删除产品的标识或版本无效。";
        }

        var duplicateUid = FindDuplicateUid(deleteItems.Select(item => item.Uid));
        if (duplicateUid.HasValue)
            return $"删除失败：产品 {duplicateUid.Value} 在本次提交中重复。";

        var ids = deleteItems.Select(item => item.Uid).ToArray();
        var existingProducts = await _repository.GetByIdListAsync(ids, withUpdLock: true);
        var missingIds = ids.Except(existingProducts.Select(product => product.Uid)).ToArray();
        return missingIds.Length > 0
            ? "删除失败：包含不存在或已删除的产品：" + string.Join(", ", missingIds)
            : null;
    }

    private async Task<string?> ValidateCustomerSuppliedAsync(ProductDto dto, Guid uid)
    {
        if (!string.Equals(dto.MaterialType, SwiftExport.Contracts.SysEnums.StaticEnums.MaterialTypesEnum.客供件, StringComparison.Ordinal))
            return null;
        var product = new Product
        {
            Uid = uid,
            Sku = dto.Sku?.Trim() ?? string.Empty,
            MaterialType = dto.MaterialType,
            SupplyType = dto.SupplyType,
            BomVersion = dto.BomVersion,
            BomNo = dto.BomNo
        };
        var hasBom = await bomRepository.HasActiveBomForProductAsync(uid, dto.BomNo);
        return CustomerSuppliedMaterialPolicy.ValidateProduct(product, hasBom);
    }


    /// <summary>在自有事务中统一产品单项与批量写入，先校验资料和受影响 BOM 引用，任一失败回滚全部操作。</summary>
    public async Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest batchReq)
    {
        if (batchReq is null)
            return ServiceResult<ProductBatchSaveResponse>.Fail("请求不能为空。");

        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(item => item is null)
            || batchReq.UpdateItems.Any(item => item is null)
            || batchReq.DeleteItems.Any(item => item is null))
            return ServiceResult<ProductBatchSaveResponse>.Fail("批量保存明细不能包含空项。");
        if (batchReq.CreateItems.Any(item => item.Data is null)
            || batchReq.UpdateItems.Any(item => item.Data is null))
            return ServiceResult<ProductBatchSaveResponse>.Fail("产品保存数据不能为空。");
        if (!batchReq.HasChanges)
            return ServiceResult<ProductBatchSaveResponse>.Fail("没有需要保存的数据。");
        if (_session.Transaction is not null)
            return ServiceResult<ProductBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始产品批量保存。");

        var response = new ProductBatchSaveResponse { TotalCount = batchReq.TotalCount };
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();

            var validationError = await ValidateBatchSaveAsync(batchReq);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                _session.Transaction.Rollback();
                return ServiceResult<ProductBatchSaveResponse>.Fail(validationError);
            }

            foreach (var item in batchReq.CreateItems)
            {
                var writeResult = await _repository.AddAsync(item.Data.Adapt<Product>());
                response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                EnsureWriteSucceeded(writeResult, "新增产品失败。");
            }

            foreach (var item in batchReq.UpdateItems)
            {
                var entity = item.Data.Adapt<Product>();
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var writeResult = await _repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                EnsureWriteSucceeded(writeResult, "更新产品失败。");
            }

            foreach (var item in batchReq.DeleteItems)
            {
                var writeResult = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                EnsureWriteSucceeded(writeResult, "删除产品失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<ProductBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (ProductBatchWriteException exception)
        {
            TryRollbackTransaction();
            _logger.LogWarning(exception, "产品批量保存因写入冲突失败，已回滚全部操作。");
            return ServiceResult<ProductBatchSaveResponse>.Fail(
                $"产品批量保存失败，所有操作已回滚。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "产品批量保存发生未预期异常，已回滚全部操作。");
            return ServiceResult<ProductBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected,
                "产品批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>先校验既有资料规则，再以本次全部候选产品校验 BOM 边界，避免按行校验遗漏同时修改的角色。</summary>
    private async Task<string?> ValidateBatchSaveAsync(ProductBatchSaveRequest request)
    {
        var errors = new StringBuilder();

        var duplicateOperationIds = request.UpdateItems
            .Select(item => item.Uid)
            .Intersect(request.DeleteItems.Select(item => item.Uid))
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .ToArray();
        if (duplicateOperationIds.Length > 0)
            errors.AppendLine("同一产品不能同时修改和删除：" + string.Join(", ", duplicateOperationIds));

        var createSkus = request.CreateItems
            .Where(item => item.Data is not null && !string.IsNullOrWhiteSpace(item.Data.Sku))
            .Select(item => item.Data.Sku.Trim());
        var updateSkus = request.UpdateItems
            .Where(item => item.Data is not null && !string.IsNullOrWhiteSpace(item.Data.Sku))
            .Select(item => item.Data.Sku.Trim());
        var crossOperationSku = createSkus
            .Intersect(updateSkus, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (crossOperationSku is not null)
            errors.AppendLine($"同一 SKU 不能同时新增和修改：{crossOperationSku}。");

        AppendError(errors, request.CreateItems.Count > 0
            ? await ValidateBeforeCreateAsync(request.CreateItems)
            : null);
        AppendError(errors, request.UpdateItems.Count > 0
            ? await ValidateBeforeUpdateAsync(request.UpdateItems)
            : null);
        AppendError(errors, request.DeleteItems.Count > 0
            ? await ValidateBeforeDeleteAsync(request.DeleteItems)
            : null);

        return errors.Length == 0
            ? await ValidateBomSaveBoundaryAsync(request)
            : errors.ToString().Trim();
    }

    /// <summary>批量确认制造商和参考仓库仍存在；只对新选或改选的目标要求启用。</summary>
    private async Task<string?> ValidateMasterDataReferencesAsync(IEnumerable<(ProductDto Data, Product? Existing)> source)
    {
        var rows = source.ToArray();
        var items = rows.Select(row => row.Data).ToArray();
        var colorIds = items.Select(item => item.ColorId)
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .ToArray();
        if (colorIds.Length > 0)
        {
            var existingItems = await colorRepository.GetByIdListAsync(colorIds);
            var missingIds = colorIds.Except(existingItems.Select(item => item.Uid)).ToArray();
            if (missingIds.Length > 0)
                return "产品颜色不存在或已失效：" + string.Join(", ", missingIds);
        }

        var manufacturerIds = rows
            .Where(row => row.Data.ManufacturerId.HasValue)
            .Select(row => row.Data.ManufacturerId!.Value)
            .Distinct().ToArray();
        var changedManufacturerIds = rows
            .Where(row => row.Data.ManufacturerId.HasValue
                && row.Data.ManufacturerId != row.Existing?.ManufacturerId)
            .Select(row => row.Data.ManufacturerId!.Value)
            .Distinct().ToArray();
        // 采购供应商与制造商共享档案，但分别保留各自的关联语义与错误提示。
        var supplierIds = ProductDefaultSupplierPolicy.GetReferencedSupplierIds(items)
            .Concat(manufacturerIds).Distinct().ToArray();
        IReadOnlyList<Supplier> suppliers = [];
        if (supplierIds.Length > 0)
        {
            suppliers = await supplierRepository.GetByIdListAsync(supplierIds);
        }

        var supplierError = ProductDefaultSupplierPolicy.Normalize(items, suppliers);
        if (supplierError is not null) return supplierError;

        // 旧关联停用后可保留，但关联已删除或不存在时不能静默保存悬空引用。
        if (manufacturerIds.Length > 0)
        {
            var existingIds = suppliers.Select(item => item.Uid).ToHashSet();
            var missing = manufacturerIds.Except(existingIds).ToArray();
            if (missing.Length > 0)
                return "制造商不存在或已删除，请清除或重新选择：" + string.Join(", ", missing);
            var activeIds = suppliers.Where(item => item.IsActive).Select(item => item.Uid).ToHashSet();
            var invalid = changedManufacturerIds.Except(activeIds).ToArray();
            if (invalid.Length > 0)
                return "新选或改选的制造商已停用：" + string.Join(", ", invalid);
        }

        var warehouseIds = rows
            .Where(row => row.Data.DefaultWarehouseSiteId.HasValue)
            .Select(row => row.Data.DefaultWarehouseSiteId!.Value)
            .Distinct().ToArray();
        var changedWarehouseIds = rows
            .Where(row => row.Data.DefaultWarehouseSiteId.HasValue
                && row.Data.DefaultWarehouseSiteId != row.Existing?.DefaultWarehouseSiteId)
            .Select(row => row.Data.DefaultWarehouseSiteId!.Value)
            .Distinct().ToArray();
        if (warehouseIds.Length > 0)
        {
            var warehouses = await warehouseRepository.GetByIdsAsync(warehouseIds);
            var existingIds = warehouses.Select(item => item.Uid).ToHashSet();
            var missing = warehouseIds.Except(existingIds).ToArray();
            if (missing.Length > 0)
                return "默认仓库不存在或已删除，请清除或重新选择：" + string.Join(", ", missing);
            var activeIds = warehouses.Where(item => item.IsActive).Select(item => item.Uid).ToHashSet();
            var invalid = changedWarehouseIds.Except(activeIds).ToArray();
            if (invalid.Length > 0)
                return "新选或改选的默认仓库已停用：" + string.Join(", ", invalid);
        }

        return null;
    }

    private async Task<string?> ValidateAndNormalizeUnitsAsync(IEnumerable<ProductUnitValidationItem> source)
    {
        var items = source.ToArray();
        var dictionaryItems = await dictDataRepository.GetByCodeAsync(DictCodes.Unit);
        return ProductUnitPolicy.Normalize(items, dictionaryItems);
    }

    private static string? ValidateProduct(ProductDto? product, int? rowIndex, string operation)
    {
        if (product is null)
            return $"第 {FormatRowIndex(rowIndex)}：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(product.Sku))
            return $"第 {FormatRowIndex(rowIndex)}：SKU 不能为空。";
        if (product.BomVersion < 0)
            return $"第 {FormatRowIndex(rowIndex)}：BOM 版本不能小于 0。";
        if (product.CNYPrice < 0 || product.USDPrice < 0)
            return $"第 {FormatRowIndex(rowIndex)}：产品价格不能小于 0。";
        if (product.DefaultWarehouseSiteId == Guid.Empty || product.ManufacturerId == Guid.Empty)
            return $"第 {FormatRowIndex(rowIndex)}：默认仓库或制造商标识不能是空 Guid。";

        // 型号是独立资料：去除首尾空白后才校验长度，不因制造商被清除而清空。
        product.ManufacturerModel = string.IsNullOrWhiteSpace(product.ManufacturerModel)
            ? null
            : product.ManufacturerModel.Trim();
        if (product.ManufacturerModel?.Length > 200)
            return $"第 {FormatRowIndex(rowIndex)}：制造商型号不能超过 200 个字符。";

        product.Sku = product.Sku.Trim();
        product.MasterSku = product.MasterSku?.Trim();
        product.BomNo = product.BomNo?.Trim();
        product.LinkSku = product.LinkSku?.Trim();
        return null;
    }

    private static string? FindDuplicateSku(IEnumerable<string?> skus) => skus
        .Where(sku => !string.IsNullOrWhiteSpace(sku))
        .Select(sku => sku!.Trim())
        .GroupBy(sku => sku, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    private static Guid? FindDuplicateUid(IEnumerable<Guid> uids) => uids
        .Where(uid => uid != Guid.Empty)
        .GroupBy(uid => uid)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";

    private static void AppendError(StringBuilder builder, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) builder.AppendLine(error);
    }

    private static void EnsureWriteSucceeded(RepositoryWriteResult writeResult, string fallbackMessage)
    {
        if (!writeResult.Success)
            throw new ProductBatchWriteException(writeResult.Message ?? fallbackMessage);
    }

    private void TryRollbackTransaction()
    {
        try
        {
            _session.Transaction?.Rollback();
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "产品批量保存事务回滚失败。");
        }
    }

    private sealed class ProductBatchWriteException(string message) : Exception(message);


}
