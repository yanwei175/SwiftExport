using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;
namespace SwiftExport.Application.Services.Products;

public partial class ProductService(IProductRepository productRepository, ServiceBaseContext context,
    IProductCategoryRepository categoryRepository,
    IProductColorRepository colorRepository,
    ISupplierRepository supplierRepository,
    IBomRepository bomRepository,
    ISysDictDataRepository dictDataRepository)
    : BaseService<Product, ProductDto>(productRepository, context), IProductService
{
    private readonly ILogger<ProductService> _logger = context.LoggerFactory.CreateLogger<ProductService>();

    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<ProductDto>> createItems)
    {
        foreach (var item in createItems)
        {
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

        var referenceError = await ValidateMasterDataReferencesAsync(createItems.Select(item => item.Data));
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

        var referenceError = await ValidateMasterDataReferencesAsync(updateItems.Select(item => item.Data));
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


    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync()
    {
        return ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll");
    }

    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        // 使用特有的 _customerProductRepository 调用特有方法
        var list = await productRepository.GetAllCustomerCodeListAsync();
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合");

        return ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    // 实现特有的业务逻辑：根据 SKU 查询
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var entity = await productRepository.GetBySkuAsync(sku);
        if (entity == null)
            return ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品");

        return ServiceResult<ProductDto>.Ok(entity.Adapt<ProductDto>());
    }

    public async Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request)
    {
        try
        {
            var result = await productRepository.GetPagedAsync(request);
            if (result == null)
                return ServiceResult<PagedResponseBase<ProductDto>>.Fail("未找到数据");
            return ServiceResult<PagedResponseBase<ProductDto>>.Ok(result);

        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<ProductDto>>.Fail(ex.Message);
        }

    }

    public async Task<ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>> GetCategoryAssignmentPageAsync(
        ProductCategoryAssignmentSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Fail("查询条件不能为空。");
        if (request.TargetCategoryId == Guid.Empty)
            return ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Fail("请选择目标分类。");

        var categories = await categoryRepository.GetByIdListAsync([request.TargetCategoryId]);
        if (categories.Count != 1)
            return ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Fail("目标分类不存在或已失效。");

        request.PageIndex = Math.Max(1, request.PageIndex);
        request.PageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var result = await productRepository.GetCategoryAssignmentPageAsync(request, cancellationToken);
        return ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Ok(result);
    }

    public async Task<ServiceResult<AssignProductsToCategoryResult>> AssignCategoryAsync(
        AssignProductsToCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return ServiceResult<AssignProductsToCategoryResult>.Fail("归类请求不能为空。");
        if (request.TargetCategoryId == Guid.Empty)
            return ServiceResult<AssignProductsToCategoryResult>.Fail("请选择有效的目标分类。");

        request.Items ??= [];
        if (request.Items.Count == 0)
            return ServiceResult<AssignProductsToCategoryResult>.Fail("请选择需要归类的产品。");
        if (request.Items.Count > 200)
            return ServiceResult<AssignProductsToCategoryResult>.Fail("单次最多归类 200 个产品。");
        if (request.Items.Any(item => item is null || item.ProductId == Guid.Empty || item.ExpectedVersion <= 0))
            return ServiceResult<AssignProductsToCategoryResult>.Fail("归类产品标识或版本无效。");

        var duplicateId = request.Items
            .GroupBy(item => item.ProductId)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateId.HasValue)
            return ServiceResult<AssignProductsToCategoryResult>.Fail($"产品 {duplicateId.Value} 在本次请求中重复。");
        if (_session.Transaction is not null)
            return ServiceResult<AssignProductsToCategoryResult>.Fail("当前请求已存在未完成事务，不能重复开始产品归类。");

        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();

            var categories = await categoryRepository.GetByIdListAsync(
                [request.TargetCategoryId],
                withUpdLock: true);
            if (categories.Count != 1)
            {
                _session.Transaction.Rollback();
                return ServiceResult<AssignProductsToCategoryResult>.Fail("目标分类不存在或已失效。");
            }

            var productIds = request.Items.Select(item => item.ProductId).ToArray();
            var products = await productRepository.GetByIdListAsync(productIds, withUpdLock: true);
            if (products.Count != productIds.Length)
            {
                _session.Transaction.Rollback();
                return ServiceResult<AssignProductsToCategoryResult>.Fail("部分产品不存在或已被删除，请刷新后重试。");
            }

            var productMap = products.ToDictionary(product => product.Uid);
            foreach (var item in request.Items)
            {
                var product = productMap[item.ProductId];
                if (product.Version != item.ExpectedVersion)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<AssignProductsToCategoryResult>.Fail(
                        $"产品 {product.Sku} 已被其他用户修改，请刷新后重试。");
                }

                if (product.CategoryId == request.TargetCategoryId)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<AssignProductsToCategoryResult>.Fail(
                        $"产品 {product.Sku} 已属于目标分类，请刷新后重试。");
                }
            }

            var response = new AssignProductsToCategoryResult
            {
                TargetCategoryId = request.TargetCategoryId
            };
            foreach (var item in request.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var writeResult = await productRepository.AssignCategoryAsync(
                    item.ProductId,
                    item.ExpectedVersion,
                    request.TargetCategoryId,
                    cancellationToken);
                if (!writeResult.Success)
                    throw new ProductCategoryAssignmentException(writeResult.Message ?? "产品归类失败。");

                response.Items.Add(new AssignedProductVersionResult
                {
                    ProductId = item.ProductId,
                    OldVersion = item.ExpectedVersion,
                    NewVersion = writeResult.NewVersion
                        ?? throw new ProductCategoryAssignmentException("产品归类成功但未返回新版本号。")
                });
            }

            response.UpdatedCount = response.Items.Count;
            _session.Transaction.Commit();
            return ServiceResult<AssignProductsToCategoryResult>.Ok(
                response,
                $"已将 {response.UpdatedCount} 个产品归入目标分类。");
        }
        catch (OperationCanceledException)
        {
            TryRollbackTransaction();
            throw;
        }
        catch (ProductCategoryAssignmentException exception)
        {
            TryRollbackTransaction();
            _logger.LogWarning(exception, "产品快速归类因并发冲突失败，已回滚全部操作。");
            return ServiceResult<AssignProductsToCategoryResult>.Fail(
                $"产品归类失败，所有操作已回滚。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "产品快速归类发生未预期异常，已回滚全部操作。");
            return ServiceResult<AssignProductsToCategoryResult>.Fail(
                "产品归类失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
        {
            return ServiceResult<List<ProductLookupDto>>.Ok([]);
        }

        var items = await productRepository.SearchLookupAsync(request);
        return ServiceResult<List<ProductLookupDto>>.Ok(items.ToList());
    }

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
            return ServiceResult<ProductBatchSaveResponse>.Fail(
                "产品批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

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

        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    private async Task<string?> ValidateMasterDataReferencesAsync(IEnumerable<ProductDto> products)
    {
        var items = products.ToArray();
        var categoryIds = items.Select(item => item.CategoryId)
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .ToArray();
        if (categoryIds.Length > 0)
        {
            var existingItems = await categoryRepository.GetByIdListAsync(categoryIds);
            var missingIds = categoryIds.Except(existingItems.Select(item => item.Uid)).ToArray();
            if (missingIds.Length > 0)
                return "产品分类不存在或已失效：" + string.Join(", ", missingIds);
        }

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

        var supplierIds = ProductDefaultSupplierPolicy.GetReferencedSupplierIds(items);
        IReadOnlyList<Supplier> suppliers = [];
        if (supplierIds.Length > 0)
        {
            suppliers = await supplierRepository.GetByIdListAsync(supplierIds);
        }

        return ProductDefaultSupplierPolicy.Normalize(items, suppliers);
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

    private sealed class ProductCategoryAssignmentException(string message) : Exception(message);

}
