using SwiftExport.Application.Interfaces.Systems.ExchangeRates;
using SwiftExport.Application.Services.Systems.Internal;
using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Core.Interfaces.Systems.ExchangeRates;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems.ExchangeRates;

/// <summary>
/// 货币汇率服务。
/// </summary>
public partial class SysDicCurrencyExchangeService(
    ISysDicCurrencyExchangeRepository repository,
    ISysDicCurrencyRepository currencyRepository,
    ServiceBaseContext context)
    : BaseService<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>(repository, context), ISysDicCurrencyExchangeService
{
    private readonly ILogger<SysDicCurrencyExchangeService> _logger = context.LoggerFactory.CreateLogger<SysDicCurrencyExchangeService>();

    private Task<string?> ValidateBeforeAddAsync(List<SysDicCurrencyExchange> entities)
    {
        return Task.FromResult(ValidateEntities(entities));
    }

    private Task<string?> ValidateBeforeUpdateAsync(List<SysDicCurrencyExchange> entities)
    {
        return Task.FromResult(ValidateEntities(entities));
    }

    private static string? ValidateEntities(List<SysDicCurrencyExchange> entities)
    {
        if (entities == null || entities.Count == 0)
            return null;

        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.FromCurrencyCode))
                return "源货币代码(FromCurrencyCode)不能为空";

            if (string.IsNullOrWhiteSpace(item.ToCurrencyCode))
                return "目标货币代码(ToCurrencyCode)不能为空";

            if (string.Equals(item.FromCurrencyCode, item.ToCurrencyCode, StringComparison.OrdinalIgnoreCase))
                return "源货币代码和目标货币代码不能相同";

            if (item.Precision < 0 || item.Precision > 6)
                return "汇率精度(Precision)范围必须在 0~6 之间";

            if (item.Rate == null || item.Rate <= 0)
                return "汇率(Rate)必须大于 0";
        }

        var duplicatePair = entities
            .GroupBy(x => new
            {
                From = (x.FromCurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
                To = (x.ToCurrencyCode ?? string.Empty).Trim().ToUpperInvariant()
            })
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicatePair != null)
            return $"存在重复的汇率关系：{duplicatePair.Key.From} -> {duplicatePair.Key.To}";

        return null;
    }


    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Ok(entities.Adapt<IReadOnlyList<SysDicCurrencyExchangeDto>>())
                : ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    public async Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request)
    {
        try
        {
            return ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>.Ok(
                await repository.GetPagedAsync(request ?? new SysDicCurrencyExchangeSearchRequest()));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "分页查询货币汇率失败。");
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            return ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    public async Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest batchReq)
    {
        var requestError = SystemBatchSaveSupport.ValidateRequest(batchReq);
        if (requestError is not null) return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail(requestError);
        if (_session.Transaction is not null)
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始货币汇率批量保存。");

        var response = new SysDicCurrencyExchangeBatchSaveResponse { TotalCount = batchReq.TotalCount };
        try
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            var preparation = await PrepareBatchSaveAsync(batchReq);
            if (preparation.Error is not null)
            {
                _session.Transaction.Rollback();
                return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail(preparation.Error);
            }

            foreach (var item in batchReq.CreateItems)
            {
                var writeResult = await repository.AddAsync(item.Data.Adapt<SysDicCurrencyExchange>());
                response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "新增货币汇率失败。");
            }
            foreach (var item in batchReq.UpdateItems)
            {
                var entity = preparation.Existing[item.Uid];
                SystemBatchSaveSupport.AdaptPreservingServerFields(item.Data, entity);
                entity.Version = item.Version;
                var writeResult = await repository.UpdateAsync(entity);
                response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "更新货币汇率失败。");
            }
            foreach (var item in batchReq.DeleteItems)
            {
                var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "删除货币汇率失败。");
            }

            _session.Transaction.Commit();
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (SystemBatchWriteException exception)
        {
            SystemBatchSaveSupport.TryRollback(_session, _logger, "货币汇率批量保存");
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail($"货币汇率批量保存失败，所有操作已回滚。原因：{exception.Message}");
        }
        catch (Exception exception)
        {
            SystemBatchSaveSupport.TryRollback(_session, _logger, "货币汇率批量保存");
            _logger.LogError(exception, "货币汇率批量保存发生未预期异常。");
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "货币汇率批量保存失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }

    private async Task<CurrencyExchangeBatchPreparation> PrepareBatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest request)
    {
        foreach (var item in request.CreateItems)
        {
            var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
            if (error is not null) return CurrencyExchangeBatchPreparation.Fail(error);
        }
        foreach (var item in request.UpdateItems)
        {
            var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
            if (error is not null) return CurrencyExchangeBatchPreparation.Fail(error);
        }

        var submitted = request.CreateItems.Select(x => x.Data).Concat(request.UpdateItems.Select(x => x.Data)).ToArray();
        var currencyCodes = submitted.SelectMany(x => new[] { x.FromCurrencyCode, x.ToCurrencyCode })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var currencies = await currencyRepository.GetByCurrencyCodesAsync(currencyCodes, withUpdLock: true);
        var currencyByCode = currencies.Where(x => !string.IsNullOrWhiteSpace(x.CurrencyCode))
            .ToDictionary(x => x.CurrencyCode!, StringComparer.OrdinalIgnoreCase);
        var missingCodes = currencyCodes.Where(code => !currencyByCode.ContainsKey(code)).ToArray();
        if (missingCodes.Length > 0) return CurrencyExchangeBatchPreparation.Fail("以下货币代码不存在：" + string.Join(", ", missingCodes));
        var disabledCodes = submitted.Where(x => x.IsEnabled)
            .SelectMany(x => new[] { x.FromCurrencyCode, x.ToCurrencyCode })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => !currencyByCode[code].IsEnabled).ToArray();
        if (disabledCodes.Length > 0) return CurrencyExchangeBatchPreparation.Fail("启用汇率不能引用停用货币：" + string.Join(", ", disabledCodes));

        var all = await repository.GetAllForUpdateAsync();
        var existing = all.ToDictionary(x => x.Uid);
        var targetIds = request.UpdateItems.Select(x => x.Uid).Concat(request.DeleteItems.Select(x => x.Uid)).Distinct().ToArray();
        var missing = targetIds.Where(uid => !existing.ContainsKey(uid)).ToArray();
        if (missing.Length > 0) return CurrencyExchangeBatchPreparation.Fail("修改或删除失败，以下货币汇率不存在：" + string.Join(", ", missing));

        var deleteIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
        var updateById = request.UpdateItems.ToDictionary(x => x.Uid);
        var finalPairs = all.Where(x => !deleteIds.Contains(x.Uid))
            .Select(x => updateById.TryGetValue(x.Uid, out var update)
                ? new CurrencyPair(update.Data.FromCurrencyCode, update.Data.ToCurrencyCode, update.Data.IsEnabled)
                : new CurrencyPair(x.FromCurrencyCode ?? string.Empty, x.ToCurrencyCode ?? string.Empty, x.IsEnabled))
            .Concat(request.CreateItems.Select(x => new CurrencyPair(x.Data.FromCurrencyCode, x.Data.ToCurrencyCode, x.Data.IsEnabled)))
            .Where(x => x.IsEnabled)
            .ToArray();
        var duplicate = finalPairs.GroupBy(x => $"{x.From}|{x.To}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) return CurrencyExchangeBatchPreparation.Fail($"启用的货币汇率关系不能重复：{duplicate.First().From} -> {duplicate.First().To}。");
        return CurrencyExchangeBatchPreparation.Ok(existing);
    }

    internal static string? NormalizeAndValidate(SysDicCurrencyExchangeDto? dto, int? rowIndex, string operation)
    {
        var row = SystemBatchSaveSupport.FormatRow(rowIndex);
        if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
        if (string.IsNullOrWhiteSpace(dto.FromCurrencyCode)) return $"第 {row} 行：源货币代码不能为空。";
        if (string.IsNullOrWhiteSpace(dto.ToCurrencyCode)) return $"第 {row} 行：目标货币代码不能为空。";
        dto.FromCurrencyCode = dto.FromCurrencyCode.Trim().ToUpperInvariant();
        dto.ToCurrencyCode = dto.ToCurrencyCode.Trim().ToUpperInvariant();
        if (string.Equals(dto.FromCurrencyCode, dto.ToCurrencyCode, StringComparison.OrdinalIgnoreCase))
            return $"第 {row} 行：源货币代码和目标货币代码不能相同。";
        if (dto.Precision is < 0 or > 6) return $"第 {row} 行：汇率精度必须在 0 到 6 之间。";
        if (dto.Rate is null or <= 0) return $"第 {row} 行：汇率必须大于 0。";
        dto.Remark = dto.Remark?.Trim();
        return null;
    }

    private sealed record CurrencyPair(string From, string To, bool IsEnabled);

    private sealed record CurrencyExchangeBatchPreparation(string? Error, Dictionary<Guid, SysDicCurrencyExchange> Existing)
    {
        public static CurrencyExchangeBatchPreparation Fail(string error) => new(error, []);
        public static CurrencyExchangeBatchPreparation Ok(Dictionary<Guid, SysDicCurrencyExchange> existing) => new(null, existing);
    }
}
