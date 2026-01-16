using AutoMapper;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Base;

public class BaseService<TEntity, TDto>(IRepository<TEntity> repository, IMapper mapper) : IBaseService<TEntity, TDto> where TEntity : BaseEntity where TDto : BaseDto
{
    protected readonly IRepository<TEntity> _repository = repository;
    protected readonly IMapper _mapper = mapper;

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
        if (entity == null) return ServiceResult<TDto>.Fail("未找到数据");
        return ServiceResult<TDto>.Ok(_mapper.Map<TDto>(entity));
    }
    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<int> idList)
    {
        var entities = await _repository.GetByIdListAsync(idList);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<TDto>>.Ok(_mapper.Map<IReadOnlyList<TDto>>(entities));
    }
    public virtual async Task<ServiceResult<TDto?>> GetSingleEntityAsync()
    {
        var entity = await _repository.GetSingleEntityAsync();
        return entity == null ? ServiceResult<TDto?>.Fail("未找到数据") : ServiceResult<TDto?>.Ok(_mapper.Map<TDto>(entity));
    }
    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<TDto>>.Ok(_mapper.Map<IReadOnlyList<TDto>>(entities));
    }
    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null)
    {
        var entities = await _repository.GetListAsync(whereSql, param);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<TDto>>.Ok(_mapper.Map<IReadOnlyList<TDto>>(entities));
    }
    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria)
    {
        var entities = await _repository.GetListAsync(criteria);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<TDto>>.Ok(_mapper.Map<IReadOnlyList<TDto>>(entities));
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object noNameObj)
    {
        var entities = await _repository.GetListAsync(noNameObj);
        if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        return ServiceResult<IReadOnlyList<TDto>>.Ok(_mapper.Map<IReadOnlyList<TDto>>(entities));
    }

    public virtual async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")
    {
        var entities = await _repository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
        return ServiceResult<PagedResultDto<TDto>>.Ok(new PagedResultDto<TDto>
        {
            Items = _mapper.Map<IReadOnlyList<TDto>>(entities.Items),
            TotalCount = entities.TotalCount,
            PageSize = entities.PageSize,
            PageIndex = entities.PageIndex
        });
    }


    public virtual async Task<ServiceResult<int>> AddAsync(TDto dto)
    {
        var entity = _mapper.Map<TEntity>(dto);
        entity.CreatedTime = DateTime.Now;
        entity.IsDeleted = 0;

        var id = await _repository.AddAsync(entity);
        return ServiceResult<int>.Ok(id);
    }

    public virtual async Task<ServiceResult<int>> UpdateAsync(TDto dto)
    {
        var existing = await _repository.GetByIdAsync(dto.Id);
        if (existing == null) return ServiceResult<int>.Fail("数据不存在");
        _mapper.Map(dto, existing); // 覆盖属性
        return ServiceResult<int>.Ok(await _repository.UpdateAsync(existing));
    }
    public virtual async Task<ServiceResult<int>> DeleteSoftAsync(int id)
    {
        return ServiceResult<int>.Ok(await _repository.DeleteSoftAsync(id));
    }


    public virtual async Task<ServiceResult<int>> DeleteAsync(int id)
    {
        return ServiceResult<int>.Ok(await _repository.DeleteAsync(id));
    }
    public virtual async Task<ServiceResult<IReadOnlyList<int>>> AddRangeAsync(IEnumerable<TDto> dtoList)
    {
        var entities = dtoList.Select(dto => _mapper.Map<TEntity>(dto)).ToList();
        return ServiceResult<IReadOnlyList<int>>.Ok(await _repository.AddRangeAsync(entities));
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<int> idList)
    {
        return ServiceResult<int>.Ok(await _repository.DeleteRangeAsync(idList));
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<int> idList)
    {
        return ServiceResult<int>.Ok(await _repository.DeleteRangeSoftAsync(idList));
    }

    public virtual async Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList)
    {
        var entities = dtoList.Select(dto => _mapper.Map<TEntity>(dto));
        return ServiceResult<int>.Ok(await _repository.UpdateRangeAsync(entities));
    }
}