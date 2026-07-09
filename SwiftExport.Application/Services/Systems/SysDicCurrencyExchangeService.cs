using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems;

/// <summary>
/// 货币汇率服务。
/// </summary>
public partial class SysDicCurrencyExchangeService(ISysDicCurrencyExchangeRepository repository, ServiceBaseContext context)
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
            return ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Fail(ex.Message);
        }
    }

    public Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request)
    {
        throw new NotImplementedException();
    }

    public Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest batchReq)
    {
        throw new NotImplementedException();
    }
}
