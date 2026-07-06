using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;
using System.Text;

using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
namespace SwiftExport.Application.Services.Systems;

/// <summary>
/// 货币汇率服务。
/// </summary>
public partial class SysDicCurrencyExchangeService(ISysDicCurrencyExchangeRepository repository, ServiceBaseContext context) : ISysDicCurrencyExchangeService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SysDicCurrencyExchangeService> _logger = context.LoggerFactory.CreateLogger<SysDicCurrencyExchangeService>();
    private readonly ISysDicCurrencyExchangeRepository _repository = repository;

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


    #region 显式基础读写

    public async Task<ServiceResult<SysDicCurrencyExchangeDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(SysDicCurrencyExchange));
                return ServiceResult<SysDicCurrencyExchangeDto>.Fail("未找到数据");
            }

            return ServiceResult<SysDicCurrencyExchangeDto>.Ok(entity.Adapt<SysDicCurrencyExchangeDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<SysDicCurrencyExchangeDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Ok(entities.Adapt<IReadOnlyList<SysDicCurrencyExchangeDto>>())
                : ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<SysDicCurrencyExchange> pagedRepository)
                return ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>.Fail($"{nameof(SysDicCurrencyExchange)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>.Ok(new PagedResultDto<SysDicCurrencyExchangeDto>
            {
                Items = paged.Items.Adapt<List<SysDicCurrencyExchangeDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(SysDicCurrencyExchangeDto dto)
    {
        try
        {
            var entity = dto.Adapt<SysDicCurrencyExchange>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _repository.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(SysDicCurrencyExchangeDto dto)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _repository.UpdateAsync(existing);
            return ServiceResult<int>.Ok(1);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _repository.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new SysDicCurrencyExchangeBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<SysDicCurrencyExchange>>();
        var updateList = batchReq.UpdateList?.Adapt<List<SysDicCurrencyExchange>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (createList is { Count: > 0 })
            {
                var error = await ValidateBeforeAddAsync(createList);
                if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
            }

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _repository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
                {
                    var error = await ValidateBeforeUpdateAsync(updateList);
                    if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
                }
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<SysDicCurrencyExchange> batchRepository)
                return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail($"{nameof(SysDicCurrencyExchange)} 仓储未实现批量保存");

            if (createList is { Count: > 0 })
            {
                result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            }

            if (updateList is { Count: > 0 })
            {
                await batchRepository.UpdateRangeAsync(updateList);
                result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            }

            if (deleteIds is { Count: > 0 })
            {
                await batchRepository.DeleteRangeSoftAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}
