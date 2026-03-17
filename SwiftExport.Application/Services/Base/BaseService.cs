using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Base;

public class BaseService<TEntity, TDto>(IRepository<TEntity> repository, ILogger logger) : IBaseService<TEntity, TDto>
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    protected readonly IRepository<TEntity> _repository = repository;
    protected readonly ILogger _logger = logger;

    protected string EntityName => typeof(TEntity).Name;

    public virtual async Task<bool> ExistsIdAsync(int id)
    {
        return await _repository.ExistsIdAsync(id);
    }

    public virtual async Task<ServiceResult<IReadOnlyList<int>>> GetExistingIdListAsync(IEnumerable<int> idList)
    {
        var rst = await _repository.GetExistingIdListAsync(idList);
        if (!rst.Any()) return ServiceResult<IReadOnlyList<int>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<int>>.Ok(rst);
    }

    public virtual async Task<ServiceResult<TDto>> GetByIdAsync(int id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity == null)
        {
            // ★ 包裹 Warning 级别
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("查询失败：未找到 ID 为 {Id} 的 {EntityName} 数据", id, EntityName);
            }
            return ServiceResult<TDto>.Fail("未找到数据");
        }

        return ServiceResult<TDto>.Ok(entity.Adapt<TDto>());
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<int> idList)
    {
        var entities = await _repository.GetByIdListAsync(idList);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

        return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
    }

    public virtual async Task<ServiceResult<TDto?>> GetSingleEntityAsync()
    {
        var entity = await _repository.GetSingleEntityAsync();
        return entity == null ? ServiceResult<TDto?>.Fail("未找到数据") : ServiceResult<TDto?>.Ok(entity.Adapt<TDto>());
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

        return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null)
    {
        var entities = await _repository.GetListAsync(whereSql, param);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

        return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria)
    {
        var entities = await _repository.GetListAsync(criteria);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

        return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object noNameObj)
    {
        var entities = await _repository.GetListAsync(noNameObj);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

        return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
    }

    public virtual async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")
    {
        var entities = await _repository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);

        return ServiceResult<PagedResultDto<TDto>>.Ok(new PagedResultDto<TDto>
        {
            Items = entities.Items.Adapt<IReadOnlyList<TDto>>(),
            TotalCount = entities.TotalCount,
            PageSize = entities.PageSize,
            PageIndex = entities.PageIndex
        });
    }

    public virtual async Task<ServiceResult<int>> AddAsync(TDto dto)
    {
        var entity = dto.Adapt<TEntity>();

        entity.CreatedTime = DateTime.Now;
        entity.IsDeleted = 0;

        var id = await _repository.AddAsync(entity);

        // ★ 包裹 Information 级别
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功新增 {EntityName} 数据，生成的 ID: {Id}", EntityName, id);
        }

        return ServiceResult<int>.Ok(id);
    }

    public virtual async Task<ServiceResult<int>> UpdateAsync(TDto dto)
    {
        var existing = await _repository.GetByIdAsync(dto.Id);
        if (existing == null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("修改失败：未找到 ID 为 {Id} 的 {EntityName} 数据", dto.Id, EntityName);
            }
            return ServiceResult<int>.Fail("数据不存在");
        }

        dto.Adapt(existing);
        var rst = await _repository.UpdateAsync(existing);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功修改 {EntityName} 数据，ID: {Id}", EntityName, dto.Id);
        }

        return ServiceResult<int>.Ok(rst);
    }

    public virtual async Task<ServiceResult<int>> DeleteSoftAsync(int id)
    {
        var rst = await _repository.DeleteSoftAsync(id);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功逻辑删除 {EntityName} 数据，ID: {Id}", EntityName, id);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public virtual async Task<ServiceResult<int>> DeleteAsync(int id)
    {
        var rst = await _repository.DeleteAsync(id);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功物理删除 {EntityName} 数据，ID: {Id}", EntityName, id);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public virtual async Task<ServiceResult<IReadOnlyList<int>>> AddRangeAsync(IEnumerable<TDto> dtoList)
    {
        var entities = dtoList.Adapt<List<TEntity>>();
        var ids = await _repository.AddRangeAsync(entities);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功批量新增 {Count} 条 {EntityName} 数据", entities.Count, EntityName);
        }

        return ServiceResult<IReadOnlyList<int>>.Ok(ids);
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<int> idList)
    {
        var ids = idList.ToList();
        var rst = await _repository.DeleteRangeAsync(ids);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功批量物理删除 {Count} 条 {EntityName} 数据", ids.Count, EntityName);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<int> idList)
    {
        var ids = idList.ToList();
        var rst = await _repository.DeleteRangeSoftAsync(ids);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功批量逻辑删除 {Count} 条 {EntityName} 数据", ids.Count, EntityName);
        }
        return ServiceResult<int>.Ok(rst);
    }

    public virtual async Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList)
    {
        var entities = dtoList.Adapt<List<TEntity>>();
        var rst = await _repository.UpdateRangeAsync(entities);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("成功批量修改 {Count} 条 {EntityName} 数据", entities.Count, EntityName);
        }
        return ServiceResult<int>.Ok(rst);
    }
}