using Microsoft.Extensions.Logging;
using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;

namespace SwiftExport.Application.Services.Suppliers;

public sealed class SupplierService(
    ISupplierRepository repository,
    ISupplierProductRepository supplierProductRepository,
    ServiceBaseContext context)
    : BaseService<Supplier, SupplierDto>(repository, context), ISupplierService
{
    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return ServiceResult<IReadOnlyList<SupplierDto>>.Ok(entities.Adapt<IReadOnlyList<SupplierDto>>());
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<IReadOnlyList<SupplierDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    public async Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request)
    {
        try
        {
            return ServiceResult<PagedResponseBase<SupplierDto>>.Ok(await repository.GetPagedAsync(request));
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<SupplierDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    public async Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest batchReq)
    {
        if (batchReq is null) return ServiceResult<SupplierBatchSaveResponse>.Fail("请求不能为空。");
        if (!batchReq.HasChanges) return ServiceResult<SupplierBatchSaveResponse>.Fail("没有需要保存的数据。");

        var validationError = await ValidateBatchSaveAsync(batchReq);
        if (!string.IsNullOrWhiteSpace(validationError))
            return ServiceResult<SupplierBatchSaveResponse>.Fail(validationError);

        var response = new SupplierBatchSaveResponse { TotalCount = batchReq.TotalCount };
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var item in batchReq.CreateItems)
            {
                var writeResult = await _repository.AddAsync(item.Data.Adapt<Supplier>());
                response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "新增供应商失败。");
            }

            foreach (var item in batchReq.UpdateItems)
            {
                var entity = item.Data.Adapt<Supplier>();
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var writeResult = await _repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "更新供应商失败。");
            }

            foreach (var item in batchReq.DeleteItems)
            {
                var writeResult = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                if (!writeResult.Success) throw new InvalidOperationException(writeResult.Message ?? "删除供应商失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<SupplierBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SupplierBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task<string?> ValidateBatchSaveAsync(SupplierBatchSaveRequest request)
    {
        var errors = new StringBuilder();
        var submittedCodes = request.CreateItems
            .Where(item => item.Data is not null)
            .Select(item => item.Data.SupplierCode?.Trim())
            .Concat(request.UpdateItems.Where(item => item.Data is not null).Select(item => item.Data.SupplierCode?.Trim()))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();
        var duplicates = submittedCodes
            .GroupBy(code => code!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
            errors.AppendLine("本次提交中存在重复供应商代码：" + string.Join(", ", duplicates));

        AppendError(errors, request.CreateItems.Count > 0 ? await ValidateBeforeCreateAsync(request.CreateItems) : null);
        AppendError(errors, request.UpdateItems.Count > 0 ? await ValidateBeforeUpdateAsync(request.UpdateItems) : null);
        AppendError(errors, request.DeleteItems.Count > 0 ? await ValidateBeforeDeleteAsync(request.DeleteItems) : null);
        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<SupplierDto>> items)
    {
        foreach (var item in items)
        {
            var error = ValidateSupplier(item.Data, item.RowIndex ?? 0, "新增");
            if (error is not null) return error;
        }

        var codes = items.Select(item => item.Data.SupplierCode.Trim()).ToArray();
        var duplicate = codes.GroupBy(code => code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) return $"新增失败：本次提交中供应商代码 {duplicate.Key} 重复。";
        var existing = await repository.GetExistingSupplierCodesAsync(codes);
        return existing.Count > 0 ? "新增失败：供应商代码已存在：" + string.Join(", ", existing) : null;
    }

    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<SupplierDto>> items)
    {
        foreach (var item in items)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0) return $"第 {item.RowIndex} 行：供应商标识或版本无效。";
            var error = ValidateSupplier(item.Data, item.RowIndex ?? 0, "修改");
            if (error is not null) return error;
        }

        var ids = items.Select(item => item.Uid).Distinct().ToArray();
        var existingIds = await _repository.GetExistingIdListAsync(ids);
        if (ids.Except(existingIds).Any()) return "修改失败：包含不存在的供应商。";
        var occupied = await repository.GetSupplierCodesUsedByOtherSuppliersAsync(
            items.Select(item => (item.Uid, item.Data.SupplierCode.Trim())));
        return occupied.Count > 0 ? "修改失败：供应商代码已被其他供应商使用：" + string.Join(", ", occupied) : null;
    }

    protected override async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> items)
    {
        if (items.Any(item => item.Uid == Guid.Empty || item.Version <= 0)) return "删除失败：供应商标识或版本无效。";
        var ids = items.Select(item => item.Uid).Distinct().ToArray();
        var existingIds = await _repository.GetExistingIdListAsync(ids);
        if (ids.Except(existingIds).Any()) return "删除失败：包含不存在的供应商。";
        var referencedIds = await supplierProductRepository.GetSupplierIdsWithProductsAsync(ids);
        return referencedIds.Count > 0
            ? "删除失败：所选供应商仍有关联供应商产品，请先删除供应商产品。"
            : null;
    }

    private static string? ValidateSupplier(SupplierDto? dto, int rowIndex, string operation)
    {
        if (dto is null) return $"第 {rowIndex} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(dto.SupplierCode)) return $"第 {rowIndex} 行：供应商代码不能为空。";
        if (string.IsNullOrWhiteSpace(dto.SupplierName)) return $"第 {rowIndex} 行：供应商名称不能为空。";
        if (string.IsNullOrWhiteSpace(dto.DocRelateNo)) return $"第 {rowIndex} 行：单据关联号不能为空。";
        if (dto.ZheKou is < 0 or > 1) return $"第 {rowIndex} 行：折扣必须在 0 到 1 之间。";
        if (dto.TaxRate is < 0 or > 1) return $"第 {rowIndex} 行：税率必须在 0 到 1 之间。";
        if (dto.DefaultLeadTime < 0) return $"第 {rowIndex} 行：默认交期不能小于 0。";
        dto.SupplierCode = dto.SupplierCode.Trim();
        dto.SupplierName = dto.SupplierName.Trim();
        dto.DocRelateNo = dto.DocRelateNo.Trim();
        return null;
    }

    private static void AppendError(StringBuilder builder, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) builder.AppendLine(error);
    }
}
