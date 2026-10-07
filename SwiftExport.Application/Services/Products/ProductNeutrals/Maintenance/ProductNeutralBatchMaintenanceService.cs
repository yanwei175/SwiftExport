using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Services.Products.ProductNeutrals.Maintenance;

/// <summary>编排跨主型号中性产品的引用校验、乐观并发和批量事务。</summary>
public sealed class ProductNeutralBatchMaintenanceService(
    IProductNeutralManagerRepository repository,
    IProductMasterRepository masterRepository,
    IProductColorRepository colorRepository,
    ServiceBaseContext context) : IProductNeutralBatchMaintenanceService
{
    private readonly IDbSession _session = context.Session;
    private readonly ILogger<ProductNeutralBatchMaintenanceService> _logger = context.LoggerFactory.CreateLogger<ProductNeutralBatchMaintenanceService>();

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
            var existing = await ValidateAsync(request, cancellationToken);

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
                // 批量维护入口不暴露默认主图编辑，更新时必须保留数据库中的当前标记。
                var entity = ToEntity(item.Data, existing[item.Uid].IsMainImage);
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

    /// <summary>校验引用、编码和并发版本，并返回已加锁实体供更新时保留非批量维护字段。</summary>
    private async Task<IReadOnlyDictionary<Guid, ProductNeutral>> ValidateAsync(
        ProductNeutralManagerBatchSaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CreateItems.Any(item => item?.Data is null)
            || request.UpdateItems.Any(item => item?.Data is null)
            || request.DeleteItems.Any(item => item is null))
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, "中性产品变更集不能包含空项。");

        var updates = request.UpdateItems.Select(item => item.Uid).ToArray();
        var deletes = request.DeleteItems.Select(item => item.Uid).ToArray();
        if (updates.Any(id => id == Guid.Empty) || deletes.Any(id => id == Guid.Empty)
            || request.UpdateItems.Any(item => item.Version <= 0) || request.DeleteItems.Any(item => item.Version <= 0)
            || updates.Distinct().Count() != updates.Length || deletes.Distinct().Count() != deletes.Length
            || updates.Intersect(deletes).Any())
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, "修改或删除的标识、版本及目标集合无效。");

        var writes = request.CreateItems.Select(item => (Id: (Guid?)null, item.Data))
            .Concat(request.UpdateItems.Select(item => (Id: (Guid?)item.Uid, item.Data))).ToArray();
        foreach (var write in writes)
        {
            var error = Validate(write.Data);
            if (error is not null) throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, error);
        }

        var normalizedCodes = writes.Select(item => item.Data.ProductCode).ToArray();
        if (normalizedCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedCodes.Length)
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, "本次提交存在重复中性产品编码。");

        var targetIds = updates.Concat(deletes).Distinct().ToArray();
        // 先观察归属，不持子行锁等待父记录；锁定旧/新父集合后必须重读，禁止按过期归属写入。
        var observed = (await repository.GetByIdListAsync(targetIds)).ToDictionary(item => item.Uid);
        if (observed.Count != targetIds.Length)
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.BusinessBlocked, "修改或删除的中性产品不存在或已删除。");
        cancellationToken.ThrowIfCancellationRequested();
        var colorIds = writes.Select(item => item.Data.ColorId).Distinct().OrderBy(id => id).ToArray();
        var colors = await colorRepository.GetByIdListAsync(colorIds, true);
        if (colors.Count != colorIds.Length)
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, "颜色不存在或已删除。");
        var requestedMasterIds = writes.Select(item => item.Data.ProductMasterId).Distinct().ToArray();
        var masterIds = requestedMasterIds.Concat(observed.Values.Select(item => item.ProductMasterId))
            .Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        var masters = await masterRepository.GetByIdListAsync(masterIds, true);
        if (requestedMasterIds.Except(masters.Select(item => item.Uid)).Any())
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, "所属主型号不存在或已删除。");
        var existing = (await repository.GetByIdListAsync(targetIds, true)).ToDictionary(item => item.Uid);
        if (existing.Count != targetIds.Length)
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.BusinessBlocked, "修改或删除的中性产品不存在或已删除。");
        if (existing.Values.Any(item => item.ProductMasterId != observed[item.Uid].ProductMasterId))
            throw new ProductNeutralBatchWriteException(ServiceErrorCodes.BusinessBlocked, "中性产品所属主型号已变化，请刷新后重试。");
        foreach (var item in request.UpdateItems)
            if (existing[item.Uid].Version != item.Version)
                throw new ProductNeutralBatchWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");
        foreach (var item in request.DeleteItems)
            if (existing[item.Uid].Version != item.Version)
                throw new ProductNeutralBatchWriteException(ServiceErrorCodes.VersionConflict, "中性产品版本已变化，请刷新后重试。");

        cancellationToken.ThrowIfCancellationRequested();
        var owners = await repository.GetByProductCodesAsync(normalizedCodes, true, cancellationToken);
        foreach (var write in writes)
            if (owners.Any(owner => string.Equals(owner.ProductCode, write.Data.ProductCode, StringComparison.OrdinalIgnoreCase)
                                    && owner.Uid != write.Id))
                throw new ProductNeutralBatchWriteException(ServiceErrorCodes.Validation, $"中性产品编码 {write.Data.ProductCode} 已被其他记录使用。");
        return existing;
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

    private static ProductNeutral ToEntity(ProductNeutralManagerWriteDto data, bool isMainImage = false) => new()
    {
        ProductMasterId = data.ProductMasterId,
        ProductCode = data.ProductCode.Trim(),
        ColorId = data.ColorId,
        DefaultSalesPriceUsd = data.DefaultSalesPriceUsd,
        DefaultExchangeRate = data.DefaultExchangeRate,
        ProfitMargin = data.ProfitMargin,
        DefaultPurchasePriceCny = data.DefaultPurchasePriceCny,
        ImagePath = NormalizeText(data.ImagePath),
        IsMainImage = isMainImage
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
        catch (ProductNeutralBatchWriteException exception)
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

    /// <summary>保留版本冲突和原失败默认分类，仅把精确编码占用视为校验失败，父颜色组合占用仍阻断业务。</summary>
    private static void EnsureWrite(RepositoryWriteResult result, string fallbackMessage)
    {
        if (result.Success) return;
        var code = result.ErrorCode?.ToUpperInvariant() switch
        {
            ServiceErrorCodes.ConcurrencyConflict => ServiceErrorCodes.VersionConflict,
            ProductErrorCodes.ProductMasterSkuOccupied or ProductErrorCodes.ProductNeutralCodeOccupied => ServiceErrorCodes.Validation,
            ProductErrorCodes.ProductNeutralMasterColorOccupied => ServiceErrorCodes.BusinessBlocked,
            _ => ServiceErrorCodes.BusinessBlocked
        };
        throw new ProductNeutralBatchWriteException(code,
            string.IsNullOrWhiteSpace(result.Message) ? fallbackMessage : result.Message);
    }

    private static string? NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ProductNeutralBatchWriteException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
