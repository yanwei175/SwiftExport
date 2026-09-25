using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Maintenance;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;

namespace SwiftExport.Application.Services.Suppliers.SupplierProducts.Maintenance;

/// <summary>维护供应商自有产品资料，校验必填供应商和可选 Product 关联并原子保存批次。</summary>
public sealed class SupplierProductMaintenanceService(
    ISupplierProductRepository repository,
    ISupplierRepository supplierRepository,
    IProductRepository productRepository,
    ServiceBaseContext context)
    : BaseService<SupplierProduct, SupplierProductDto>(repository, context), ISupplierProductMaintenanceService
{
    public override async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<SupplierProductDto> item)
    {
        var relationError = await NormalizeRelationsAsync([item.Data]);
        return relationError is null
            ? await base.AddAsync(item)
            : ServiceResult<SaveItemResult>.Fail(relationError);
    }

    public override async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<SupplierProductDto> item)
    {
        var relationError = await NormalizeRelationsAsync([item.Data]);
        return relationError is null
            ? await base.UpdateAsync(item)
            : ServiceResult<SaveItemResult>.Fail(relationError);
    }

    public async Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(SupplierProductBatchSaveRequest request)
    {
        if (request is null) return ServiceResult<SupplierProductBatchSaveResponse>.Fail("请求不能为空。");
        if (!request.HasChanges) return ServiceResult<SupplierProductBatchSaveResponse>.Fail("没有需要保存的数据。");

        var changedDtos = request.CreateItems.Select(item => item.Data)
            .Concat(request.UpdateItems.Select(item => item.Data))
            .ToList();
        var relationError = await NormalizeRelationsAsync(changedDtos);
        if (relationError is not null) return ServiceResult<SupplierProductBatchSaveResponse>.Fail(relationError);
        var validationError = await ValidateBatchSaveAsync(request);
        if (validationError is not null) return ServiceResult<SupplierProductBatchSaveResponse>.Fail(validationError);

        var response = new SupplierProductBatchSaveResponse { TotalCount = request.TotalCount };
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var item in request.CreateItems)
            {
                var writeResult = await _repository.AddAsync(item.Data.Adapt<SupplierProduct>());
                response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "新增供应商产品失败。");
            }

            foreach (var item in request.UpdateItems)
            {
                var entity = item.Data.Adapt<SupplierProduct>();
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var writeResult = await _repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "更新供应商产品失败。");
            }

            foreach (var item in request.DeleteItems)
            {
                var writeResult = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "删除供应商产品失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<SupplierProductBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SupplierProductBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task<string?> NormalizeRelationsAsync(IEnumerable<SupplierProductDto?> source)
    {
        var items = source.Where(dto => dto is not null).Cast<SupplierProductDto>().ToList();
        if (items.Count == 0) return null;
        if (items.Any(dto => dto.SupplierId == Guid.Empty)) return "供应商 ID 不能为空。";
        if (items.Any(dto => dto.SkuId == Guid.Empty)) return "标准 SKU 标识无效；请清除关联后重试。";

        var suppliers = await supplierRepository.GetByIdListAsync(items.Select(dto => dto.SupplierId).Distinct());
        // 只验证真实选择的产品；空关联是独立供应商资料，不创建占位产品。
        var productIds = items.Where(dto => dto.SkuId.HasValue).Select(dto => dto.SkuId!.Value).Distinct().ToArray();
        IReadOnlyList<Product> products = productIds.Length == 0
            ? []
            : await productRepository.GetByIdListAsync(productIds);
        var supplierById = suppliers.ToDictionary(entity => entity.Uid);
        var productById = products.ToDictionary(entity => entity.Uid);
        foreach (var dto in items)
        {
            if (!supplierById.TryGetValue(dto.SupplierId, out var supplier)) return "关联供应商不存在或已删除。";
            dto.SupplierCode = supplier.SupplierCode;
            if (dto.SkuId.HasValue)
            {
                if (!productById.TryGetValue(dto.SkuId.Value, out var product)) return "关联标准产品不存在或已删除。";
                dto.ProductSku = product.Sku;
            }
            else
            {
                dto.ProductSku = string.Empty;
            }
        }
        return null;
    }

    private async Task<string?> ValidateBatchSaveAsync(SupplierProductBatchSaveRequest request)
    {
        var errors = new StringBuilder();
        var submitted = request.CreateItems.Select(item => item.Data)
            .Concat(request.UpdateItems.Select(item => item.Data))
            .Where(dto => dto is not null)
            .ToList();
        var duplicate = submitted
            .Where(dto => dto.SupplierId != Guid.Empty && !string.IsNullOrWhiteSpace(dto.SupplierSku))
            .GroupBy(dto => (dto.SupplierId, dto.SupplierSku.Trim().ToUpperInvariant()))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) errors.AppendLine($"本次提交中供应商料号重复：{duplicate.First().SupplierSku}");

        AppendError(errors, request.CreateItems.Count > 0 ? await ValidateBeforeCreateAsync(request.CreateItems) : null);
        AppendError(errors, request.UpdateItems.Count > 0 ? await ValidateBeforeUpdateAsync(request.UpdateItems) : null);
        AppendError(errors, request.DeleteItems.Count > 0 ? await ValidateBeforeDeleteAsync(request.DeleteItems) : null);
        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<SupplierProductDto>> items)
    {
        foreach (var item in items)
        {
            var error = ValidateSupplierProduct(item.Data, item.RowIndex ?? 0);
            if (error is not null) return error;
        }

        foreach (var group in items.GroupBy(item => item.Data.SupplierId))
        {
            var skus = group.Select(item => item.Data.SupplierSku.Trim()).ToArray();
            var existing = await repository.GetExistingSupplierSkuListAsync(skus, group.Key);
            if (existing.Count > 0) return "新增失败：供应商料号已存在：" + string.Join(", ", existing);
        }
        return null;
    }

    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<SupplierProductDto>> items)
    {
        foreach (var item in items)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0) return $"第 {item.RowIndex} 行：供应商产品标识或版本无效。";
            var error = ValidateSupplierProduct(item.Data, item.RowIndex ?? 0);
            if (error is not null) return error;
        }

        var ids = items.Select(item => item.Uid).Distinct().ToArray();
        var existingIds = await _repository.GetExistingIdListAsync(ids);
        if (ids.Except(existingIds).Any()) return "修改失败：包含不存在的供应商产品。";
        foreach (var group in items.GroupBy(item => item.Data.SupplierId))
        {
            var occupied = await repository.GetSupplierSkusUsedByOtherRecordsAsync(
                group.Key,
                group.Select(item => (item.Uid, item.Data.SupplierSku.Trim())));
            if (occupied.Count > 0) return "修改失败：供应商料号已被其他记录使用：" + string.Join(", ", occupied);
        }
        return null;
    }

    protected override async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> items)
    {
        if (items.Any(item => item.Uid == Guid.Empty || item.Version <= 0)) return "删除失败：供应商产品标识或版本无效。";
        var ids = items.Select(item => item.Uid).Distinct().ToArray();
        var existingIds = await _repository.GetExistingIdListAsync(ids);
        return ids.Except(existingIds).Any() ? "删除失败：包含不存在的供应商产品。" : null;
    }

    /// <summary>校验供应商资料自身字段；Product 关联为空合法，非空关联由关系规范化校验。</summary>
    internal static string? ValidateSupplierProduct(SupplierProductDto? dto, int rowIndex)
    {
        if (dto is null) return $"第 {rowIndex} 行：数据不能为空。";
        if (dto.SupplierId == Guid.Empty || string.IsNullOrWhiteSpace(dto.SupplierCode)) return $"第 {rowIndex} 行：必须关联供应商。";
        if (dto.SkuId == Guid.Empty) return $"第 {rowIndex} 行：标准 SKU 标识无效。";
        if (string.IsNullOrWhiteSpace(dto.SupplierSku)) return $"第 {rowIndex} 行：供应商料号不能为空。";
        if (dto.UnitPrice < 0 || dto.TaxPrice < 0) return $"第 {rowIndex} 行：价格不能小于 0。";
        dto.SupplierSku = dto.SupplierSku.Trim();
        return null;
    }

    private static void AppendError(StringBuilder builder, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) builder.AppendLine(error);
    }
}
