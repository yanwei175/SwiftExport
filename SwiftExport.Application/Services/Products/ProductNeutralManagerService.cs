using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Products;

/// <summary>编排跨主型号中性产品分页、引用校验、主图替换、乐观并发和批量事务。</summary>
public sealed class ProductNeutralManagerService(
    IProductNeutralManagerRepository repository,
    IProductMasterRepository masterRepository,
    IProductColorRepository colorRepository,
    ServiceBaseContext context) : IProductNeutralManagerService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ProductNeutralManagerService> _logger = context.LoggerFactory.CreateLogger<ProductNeutralManagerService>();

    /// <summary>执行只读分页查询，不修改聚合或实体状态。</summary>
    public async Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken = default) =>
        ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Ok(
            await repository.GetPagedAsync(request ?? new ProductNeutralManagerSearchRequest(), cancellationToken));

    /// <summary>验证完整批次后原子写入；任何失败均回滚，不返回部分成功。</summary>
    public async Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
        ProductNeutralManagerBatchSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !request.HasChanges)
            return ServiceResult<ProductNeutralManagerBatchSaveResponse>.Fail(ServiceErrorCodes.Validation, "没有需要保存的中性产品变更。");

        return await ExecuteTransactionAsync(async () =>
        {
            Normalize(request);
            await ValidateAsync(request, cancellationToken);
            var submittedIds = request.UpdateItems.Select(item => item.Uid)
                .Concat(request.DeleteItems.Select(item => item.Uid))
                .Where(id => id != Guid.Empty).Distinct().ToArray();

            // 只对本批次明确提交的主图候选执行自动替换；未触碰的主型号保持原状。
            var winners = ProductNeutralManagerMainImagePolicy.GetWinnerMasterIds(
                request.CreateItems.Select(item => item.Data).Concat(request.UpdateItems.Select(item => item.Data)));
            foreach (var productMasterId in winners)
                await repository.ClearMainImagesAsync(productMasterId, submittedIds, cancellationToken);

            var response = new ProductNeutralManagerBatchSaveResponse { TotalCount = request.TotalCount };
            foreach (var item in request.CreateItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await repository.AddAsync(ToEntity(item.Data));
                response.Items.Add(SaveItemResultFactory.FromCreate(item, result));
                EnsureWrite(result, "新增中性产品失败。");
            }
            foreach (var item in request.UpdateItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = ToEntity(item.Data);
                entity.Uid = item.Uid;
                entity.Version = item.Version;
                var result = await repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, result));
                EnsureWrite(result, "更新中性产品失败。");
            }
            foreach (var item in request.DeleteItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await repository.DeleteAsync(item.Uid, item.Version, true);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, result));
                EnsureWrite(result, "删除中性产品失败。");
            }

            return ServiceResult<ProductNeutralManagerBatchSaveResponse>.Ok(response, $"已保存 {response.SuccessCount} 条中性产品变更。");
        }, cancellationToken);
    }

    /// <summary>校验引用、编码、并发版本和批次最终主图候选，所有读取均位于写事务内。</summary>
    private async Task ValidateAsync(
        ProductNeutralManagerBatchSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CreateItems.Any(item => item?.Data is null)
            || request.UpdateItems.Any(item => item?.Data is null)
            || request.DeleteItems.Any(item => item is null))
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "中性产品变更集不能包含空项。");

        var updates = request.UpdateItems.Select(item => item.Uid).ToArray();
        var deletes = request.DeleteItems.Select(item => item.Uid).ToArray();
        if (updates.Any(id => id == Guid.Empty) || deletes.Any(id => id == Guid.Empty)
            || request.UpdateItems.Any(item => item.Version <= 0) || request.DeleteItems.Any(item => item.Version <= 0)
            || updates.Distinct().Count() != updates.Length || deletes.Distinct().Count() != deletes.Length
            || updates.Intersect(deletes).Any())
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "修改或删除的标识、版本及目标集合无效。");

        var writes = request.CreateItems.Select(item => (Id: (Guid?)null, item.Data))
            .Concat(request.UpdateItems.Select(item => (Id: (Guid?)item.Uid, item.Data))).ToArray();
        foreach (var write in writes)
        {
            var error = Validate(write.Data);
            if (error is not null) throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, error);
        }

        var normalizedCodes = writes.Select(item => item.Data.ProductCode).ToArray();
        if (normalizedCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedCodes.Length)
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "本次提交存在重复中性产品编码。");

        if (ProductNeutralManagerMainImagePolicy.HasMultipleWinners(writes.Select(item => item.Data)))
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "同一个主型号在本次提交中只能指定一个默认主图。");

        var targetIds = updates.Concat(deletes).Distinct().ToArray();
        var existing = (await repository.GetByIdListAsync(targetIds, true)).ToDictionary(item => item.Uid);
        if (existing.Count != targetIds.Length)
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.BusinessBlocked, "修改或删除的中性产品不存在或已删除。");
        foreach (var item in request.UpdateItems)
            if (existing[item.Uid].Version != item.Version)
                throw new ProductNeutralManagerWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");
        foreach (var item in request.DeleteItems)
            if (existing[item.Uid].Version != item.Version)
                throw new ProductNeutralManagerWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");

        cancellationToken.ThrowIfCancellationRequested();
        var masterIds = writes.Select(item => item.Data.ProductMasterId).Distinct().OrderBy(id => id).ToArray();
        var masters = await masterRepository.GetByIdListAsync(masterIds, true);
        if (masters.Count != masterIds.Length)
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "所属主型号不存在或已删除。");

        var colorIds = writes.Select(item => item.Data.ColorId).Distinct().OrderBy(id => id).ToArray();
        var colors = await colorRepository.GetByIdListAsync(colorIds, true);
        if (colors.Count != colorIds.Length)
            throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, "颜色不存在或已删除。");

        var owners = await repository.GetByProductCodesAsync(normalizedCodes, true, cancellationToken);
        foreach (var write in writes)
            if (owners.Any(owner => string.Equals(owner.ProductCode, write.Data.ProductCode, StringComparison.OrdinalIgnoreCase)
                                    && owner.Uid != write.Id))
                throw new ProductNeutralManagerWriteException(ServiceErrorCodes.Validation, $"中性产品编码 {write.Data.ProductCode} 已被其他记录使用。");

    }

    private static string? Validate(ProductNeutralManagerWriteDto data)
    {
        if (data.ProductMasterId == Guid.Empty) return "请选择所属主型号。";
        if (string.IsNullOrWhiteSpace(data.ProductCode)) return "中性产品编码不能为空。";
        if (data.ColorId == Guid.Empty) return "请选择产品颜色。";
        if (data.DefaultSalesPriceUsd < 0 || data.DefaultPurchasePriceCny < 0) return "价格不能小于零。";
        if (data.DefaultExchangeRate.HasValue && data.DefaultExchangeRate.Value <= 0) return "汇率必须大于零。";
        return null;
    }

    private static ProductNeutral ToEntity(ProductNeutralManagerWriteDto data) => new()
    {
        ProductMasterId = data.ProductMasterId,
        ProductCode = data.ProductCode.Trim(),
        ColorId = data.ColorId,
        DefaultSalesPriceUsd = data.DefaultSalesPriceUsd,
        DefaultExchangeRate = data.DefaultExchangeRate,
        ProfitMargin = data.ProfitMargin,
        DefaultPurchasePriceCny = data.DefaultPurchasePriceCny,
        ImagePath = NormalizeText(data.ImagePath),
        IsMainImage = data.IsMainImage
    };

    private static void Normalize(ProductNeutralManagerBatchSaveRequest request)
    {
        request.CreateItems ??= [];
        request.UpdateItems ??= [];
        request.DeleteItems ??= [];
        foreach (var item in request.CreateItems.Where(item => item?.Data is not null))
            item.Data.ProductCode = item.Data.ProductCode?.Trim();
        foreach (var item in request.UpdateItems.Where(item => item?.Data is not null))
            item.Data.ProductCode = item.Data.ProductCode?.Trim();
    }

    private async Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> ExecuteTransactionAsync(
        Func<Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>>> action,
        CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null)
            return ServiceResult<ProductNeutralManagerBatchSaveResponse>.Fail(ServiceErrorCodes.BusinessBlocked, "当前请求已存在未完成事务。");
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
        catch (ProductNeutralManagerWriteException exception)
        {
            TryRollback();
            _logger.LogWarning(exception, "中性产品管理保存被业务校验拒绝，事务已回滚。");
            return ServiceResult<ProductNeutralManagerBatchSaveResponse>.Fail(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            TryRollback();
            _logger.LogError(exception, "中性产品管理保存发生未预期异常，事务已回滚。");
            return ServiceResult<ProductNeutralManagerBatchSaveResponse>.Fail(ServiceErrorCodes.Unexpected, "保存中性产品失败，所有操作已回滚。");
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
        catch (Exception exception) { _logger.LogError(exception, "回滚中性产品管理事务失败。"); }
    }

    private static void EnsureWrite(RepositoryWriteResult result, string fallbackMessage)
    {
        if (result.Success) return;
        var code = string.Equals(result.ErrorCode, "CONCURRENCY_CONFLICT", StringComparison.OrdinalIgnoreCase)
            ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.BusinessBlocked;
        throw new ProductNeutralManagerWriteException(code,
            string.IsNullOrWhiteSpace(result.Message) ? fallbackMessage : result.Message);
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ProductNeutralManagerWriteException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
