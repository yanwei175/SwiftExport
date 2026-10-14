using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
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
using System.Data;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Contracts.DTOs.Errors;
namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>
/// 负责产品资料写入、批量保存和主数据引用校验；查询由产品查询服务提供。
/// </summary>
public partial class ProductMaintenanceService(IProductRepository productRepository, ServiceBaseContext context,
    IProductColorRepository colorRepository,
    ISupplierRepository supplierRepository,
    IWarehouseQueryRepository warehouseRepository,
    IBomRepository bomRepository,
    ISysDictDataRepository dictDataRepository,
    IProductAttributeRepository attributeRepository,
    IProductMasterRepository masterRepository)
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

    /// <summary>校验候选客供件为采购型无 BOM 物料；来源父字段不再代表归属，已有共享引用由保存边界统一保护。</summary>
    private Task<string?> ValidateCustomerSuppliedAsync(ProductDto dto, Guid uid)
    {
        if (!string.Equals(dto.MaterialType, SwiftExport.Contracts.SysEnums.StaticEnums.MaterialTypesEnum.客供件, StringComparison.Ordinal))
            return Task.FromResult<string?>(null);
        var product = new Product
        {
            Uid = uid,
            Sku = dto.Sku?.Trim() ?? string.Empty,
            MaterialType = dto.MaterialType,
            SupplyType = dto.SupplyType,
            BomVersion = dto.BomVersion,
            BomNo = dto.BomNo
        };
        return Task.FromResult(CustomerSuppliedMaterialPolicy.ValidateProduct(product, false));
    }


    /// <summary>在自有 Serializable 事务中统一产品单项与批量写入，校验资料和共享引用；序列化终止不自动重试，其他提交异常保持结果未知。</summary>
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
        IDbTransaction? transaction = null;
        try
        {
            transaction = _session.Connection.BeginTransaction(IsolationLevel.Serializable);
            _session.Transaction = transaction;

            var validationError = await ValidateBatchSaveAsync(batchReq);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                var rolledBack = TryRollbackTransaction(transaction);
                return ServiceResult<ProductBatchSaveResponse>.Fail(rolledBack
                    ? validationError : validationError + "事务回滚未能确认，请先核实结果。");
            }

            await WriteBatchInCurrentTransactionAsync(batchReq, response);

            // 明确序列化失败表示事务未提交；其他提交异常不能据回滚尝试推断结果，更不能自动重写。
            try { transaction.Commit(); }
            catch (Exception commitException)
            {
                TryRollbackTransaction(transaction);
                TryLogSaveFailure(LogLevel.Error, commitException, "产品保存提交失败，已尝试回滚清理。");
                if (_session.Errors.Classify(commitException) == DatabaseFailureKind.SerializationFailure)
                    return ServiceResult<ProductBatchSaveResponse>.Fail(ServiceErrorCodes.ServiceUnavailable,
                        "并发结构变化导致产品保存事务终止，本次未提交，请刷新后重新保存。");
                return ServiceResult<ProductBatchSaveResponse>.Fail(ServiceErrorCodes.OutcomeUnknown,
                    "产品保存提交结果无法确认，请先刷新并核实结果，再决定后续操作。");
            }
            return ServiceResult<ProductBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (ProductBatchWriteException exception)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Warning, exception, "产品批量保存因行写入失败终止，已尝试回滚清理。");
            return ServiceResult<ProductBatchSaveResponse>.Fail(rolledBack
                ? $"产品批量保存失败，所有操作已回滚。原因：{exception.Message}"
                : $"产品批量保存失败，事务回滚未能确认，请先核实结果。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Error, exception, "产品批量保存发生异常，已尝试回滚清理。");
            if (_session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure)
                return ServiceResult<ProductBatchSaveResponse>.Fail(ServiceErrorCodes.ServiceUnavailable,
                    "并发结构变化导致产品保存事务终止，本次未提交，请刷新后重新保存。");
            return ServiceResult<ProductBatchSaveResponse>.Fail(ServiceErrorCodes.Unexpected, rolledBack
                ? "产品批量保存失败，所有操作已回滚，请刷新后重试。"
                : "产品批量保存失败，事务未开始或回滚未能确认，请先核实结果。");
        }
        finally
        {
            // 只释放本层创建的事务；清理与日志故障不能覆盖已确认结果或遗留会话事务。
            if (transaction is not null)
            {
                try { transaction.Dispose(); }
                catch (Exception disposeException)
                {
                    TryLogSaveFailure(LogLevel.Error, disposeException, "产品保存事务释放失败，关闭连接。");
                    try { _session.Connection.Dispose(); }
                    catch (Exception connectionException)
                    {
                        TryLogSaveFailure(LogLevel.Error, connectionException, "产品保存连接释放失败。");
                    }
                }
                finally { _session.Transaction = null; }
            }
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

    /// <summary>尽力回滚本层事务并记录是否确认完成；清理和日志故障不覆盖原业务结果。</summary>
    private bool TryRollbackTransaction(IDbTransaction? transaction)
    {
        try
        {
            if (transaction is null) return false;
            transaction.Rollback();
            return true;
        }
        catch (Exception rollbackException)
        {
            TryLogSaveFailure(LogLevel.Error, rollbackException, "产品批量保存事务回滚失败。");
            return false;
        }
    }

    /// <summary>仅尽力记录保存与清理故障，失效日志提供者不能中断后续释放或替换结果。</summary>
    private void TryLogSaveFailure(LogLevel level, Exception exception, string message)
    {
        try { _logger.Log(level, exception, "{Message}", message); }
        catch { /* 不通过同一失效日志管道再次记录，保留原始结果并继续清理。 */ }
    }

    private sealed class ProductBatchWriteException(string message) : Exception(message);


}
