using SwiftExport.Core.Interfaces.Products.Colors;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Products.Colors;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;
namespace SwiftExport.Application.Services.Products.Colors;

/// <summary>维护颜色资料，删除时保护有效产品和中性产品引用。</summary>
public partial class ProductColorService(IProductColorRepository repository, ServiceBaseContext context)
    : BaseService<ProductColor, ProductColorDto>(repository, context), IProductColorService
{
    private readonly ILogger<ProductColorService> _logger = context.LoggerFactory.CreateLogger<ProductColorService>();

    #region 批量保存
    public async Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest batchReq)
    {
        if (batchReq is null)
            return ServiceResult<ProductColorBatchSaveResponse>.Fail("请求不能为空。");

        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(item => item is null)
            || batchReq.UpdateItems.Any(item => item is null)
            || batchReq.DeleteItems.Any(item => item is null))
            return ServiceResult<ProductColorBatchSaveResponse>.Fail("批量保存明细不能包含空项。");
        if (!batchReq.HasChanges)
            return ServiceResult<ProductColorBatchSaveResponse>.Fail("没有需要保存的数据。");
        if (_session.Transaction is not null)
            return ServiceResult<ProductColorBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始产品颜色批量保存。");

        var response = new ProductColorBatchSaveResponse { TotalCount = batchReq.TotalCount };
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();

            var validationError = await ValidateBatchSaveAsync(batchReq);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                _session.Transaction.Rollback();
                return ServiceResult<ProductColorBatchSaveResponse>.Fail(validationError);
            }

            foreach (var item in batchReq.CreateItems)
            {
                var writeResult = await repository.AddAsync(item.Data.Adapt<ProductColor>());
                response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                EnsureWriteSucceeded(writeResult, "新增产品颜色失败。");
            }

            foreach (var item in batchReq.UpdateItems)
            {
                var entity = item.Data.Adapt<ProductColor>();
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var writeResult = await repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                EnsureWriteSucceeded(writeResult, "更新产品颜色失败。");
            }

            foreach (var item in batchReq.DeleteItems)
            {
                var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                EnsureWriteSucceeded(writeResult, "删除产品颜色失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<ProductColorBatchSaveResponse>.Ok(
                response,
                $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (ProductColorBatchWriteException exception)
        {
            TryRollbackTransaction();
            _logger.LogWarning(exception, "产品颜色批量保存因写入冲突失败，已回滚全部操作。");
            return ServiceResult<ProductColorBatchSaveResponse>.Fail(
                $"产品颜色批量保存失败，所有操作已回滚。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            TryRollbackTransaction();
            _logger.LogError(exception, "产品颜色批量保存发生未预期异常，已回滚全部操作。");
            return ServiceResult<ProductColorBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected,
                "产品颜色批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task<string?> ValidateBatchSaveAsync(ProductColorBatchSaveRequest request)
    {
        var errors = new StringBuilder();

        var duplicateUpdateUid = FindDuplicateUid(request.UpdateItems.Select(item => item.Uid));
        if (duplicateUpdateUid is not null)
            errors.AppendLine($"同一产品颜色不能重复修改：{duplicateUpdateUid}。");
        var duplicateDeleteUid = FindDuplicateUid(request.DeleteItems.Select(item => item.Uid));
        if (duplicateDeleteUid is not null)
            errors.AppendLine($"同一产品颜色不能重复删除：{duplicateDeleteUid}。");
        var conflictingUid = request.UpdateItems.Select(item => item.Uid)
            .Intersect(request.DeleteItems.Select(item => item.Uid))
            .FirstOrDefault(uid => uid != Guid.Empty);
        if (conflictingUid != Guid.Empty)
            errors.AppendLine($"同一产品颜色不能同时修改和删除：{conflictingUid}。");

        foreach (var item in request.CreateItems)
            AppendError(errors, ValidateColor(item.Data, item.RowIndex, "新增"));
        foreach (var item in request.UpdateItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                errors.AppendLine($"第 {FormatRowIndex(item.RowIndex)} 行：修改标识或版本无效。");
            AppendError(errors, ValidateColor(item.Data, item.RowIndex, "修改"));
        }
        foreach (var item in request.DeleteItems)
        {
            if (item.Uid == Guid.Empty || item.Version <= 0)
                errors.AppendLine($"第 {FormatRowIndex(item.RowIndex)} 行：删除标识或版本无效。");
        }

        var submittedCodes = request.CreateItems
            .Where(item => item.Data is not null)
            .Select(item => item.Data.ColorCodeUnique)
            .Concat(request.UpdateItems.Where(item => item.Data is not null).Select(item => item.Data.ColorCodeUnique));
        var duplicateCode = FindDuplicateCode(submittedCodes);
        if (duplicateCode is not null)
            errors.AppendLine($"本次提交中存在重复唯一颜色代码：{duplicateCode}。");

        if (errors.Length > 0) return errors.ToString().Trim();

        var targetIds = request.UpdateItems.Select(item => item.Uid)
            .Concat(request.DeleteItems.Select(item => item.Uid))
            .Distinct()
            .ToArray();
        if (targetIds.Length > 0)
        {
            var existing = await repository.GetByIdListAsync(targetIds, withUpdLock: true);
            var missing = targetIds.Except(existing.Select(item => item.Uid)).ToArray();
            if (missing.Length > 0)
                errors.AppendLine("修改或删除失败，以下产品颜色不存在：" + string.Join(", ", missing));
        }

        var normalizedCodes = submittedCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var owners = await repository.GetByColorCodesAsync(normalizedCodes, withUpdLock: true);
        foreach (var item in request.CreateItems)
        {
            var code = item.Data.ColorCodeUnique;
            if (owners.Any(owner => string.Equals(owner.ColorCodeUnique, code, StringComparison.OrdinalIgnoreCase)))
                errors.AppendLine($"新增失败：唯一颜色代码 {code} 已存在。");
        }
        foreach (var item in request.UpdateItems)
        {
            var code = item.Data.ColorCodeUnique;
            if (owners.Any(owner => owner.Uid != item.Uid
                                    && string.Equals(owner.ColorCodeUnique, code, StringComparison.OrdinalIgnoreCase)))
                errors.AppendLine($"修改失败：唯一颜色代码 {code} 已被其他产品颜色使用。");
        }

        var deleteIds = request.DeleteItems.Select(item => item.Uid).Distinct().ToArray();
        var referencedIds = await repository.GetReferencedColorIdsAsync(deleteIds);
        if (referencedIds.Count > 0)
            errors.AppendLine("删除失败：以下产品颜色仍被产品或中性产品引用：" + string.Join(", ", referencedIds));

        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    private static string? ValidateColor(ProductColorDto? color, int? rowIndex, string operation)
    {
        if (color is null)
            return $"第 {FormatRowIndex(rowIndex)} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(color.ColorCodeUnique))
            return $"第 {FormatRowIndex(rowIndex)} 行：唯一颜色代码不能为空。";
        if (string.IsNullOrWhiteSpace(color.ColorCode))
            return $"第 {FormatRowIndex(rowIndex)} 行：颜色代码不能为空。";

        color.ColorCodeUnique = color.ColorCodeUnique.Trim();
        color.ColorCode = color.ColorCode.Trim();
        color.ColorNameEn = color.ColorNameEn?.Trim();
        color.ColorNameZh = color.ColorNameZh?.Trim();
        return null;
    }

    private static string? FindDuplicateCode(IEnumerable<string?> codes) => codes
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code!.Trim())
        .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(uid => uid != Guid.Empty)
        .GroupBy(uid => uid)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    private static string FormatRowIndex(int? rowIndex) => rowIndex?.ToString() ?? "?";

    private static void AppendError(StringBuilder builder, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) builder.AppendLine(error);
    }

    private static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success)
            throw new ProductColorBatchWriteException(result.Message ?? fallbackMessage);
    }

    private void TryRollbackTransaction()
    {
        try
        {
            _session.Transaction?.Rollback();
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "产品颜色批量保存事务回滚失败。");
        }
    }

    private sealed class ProductColorBatchWriteException(string message) : Exception(message);
    #endregion


    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<ProductColorDto>>.Ok(entities.Adapt<IReadOnlyList<ProductColorDto>>())
                : ServiceResult<IReadOnlyList<ProductColorDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<IReadOnlyList<ProductColorDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }


    public async Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request)
    {
        try
        {
            var paged = await repository.GetProductColorPagedAsync(request);
            return ServiceResult<PagedResponseBase<ProductColorDto>>.Ok(new PagedResponseBase<ProductColorDto>
            {
                Items = paged.Items.Adapt<List<ProductColorDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<ProductColorDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }




    #endregion

}
