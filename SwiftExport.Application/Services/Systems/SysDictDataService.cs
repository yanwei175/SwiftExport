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
namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 字典明细服务。
    /// </summary>
    public partial class SysDictDataService(ISysDictDataRepository repository, ServiceBaseContext context) : ISysDictDataService
    {
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SysDictDataService> _logger = context.LoggerFactory.CreateLogger<SysDictDataService>();
    private readonly ISysDictDataRepository _repository = repository;
    

    #region 显式基础读写

    public async Task<ServiceResult<SysDictDataDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(SysDictData));
                return ServiceResult<SysDictDataDto>.Fail("未找到数据");
            }

            return ServiceResult<SysDictDataDto>.Ok(entity.Adapt<SysDictDataDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<SysDictDataDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(entities.Adapt<IReadOnlyList<SysDictDataDto>>())
                : ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<SysDictDataDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<SysDictData> pagedRepository)
                return ServiceResult<PagedResultDto<SysDictDataDto>>.Fail($"{nameof(SysDictData)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<SysDictDataDto>>.Ok(new PagedResultDto<SysDictDataDto>
            {
                Items = paged.Items.Adapt<List<SysDictDataDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<SysDictDataDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(SysDictDataDto dto)
    {
        try
        {
            var entity = dto.Adapt<SysDictData>();
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

    public async Task<ServiceResult<int>> UpdateAsync(SysDictDataDto dto)
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

    public async Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new SysDictDataBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<SysDictData>>();
        var updateList = batchReq.UpdateList?.Adapt<List<SysDictData>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _repository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<SysDictDataBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<SysDictData> batchRepository)
                return ServiceResult<SysDictDataBatchSaveResponse>.Fail($"{nameof(SysDictData)} 仓储未实现批量保存");

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
            return ServiceResult<SysDictDataBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<SysDictDataBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dictCode))
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("字典编码不能为空");

            if (_repository is not IObjectQueryRepository<SysDictData> queryRepository)
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail($"{nameof(SysDictData)} 仓储未实现对象条件查询");

            var entities = await queryRepository.GetListAsync(new { DictCode = dictCode });
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(entities.Adapt<IReadOnlyList<SysDictDataDto>>())
                : ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(ex.Message);
        }
    }
    #endregion

}
}
