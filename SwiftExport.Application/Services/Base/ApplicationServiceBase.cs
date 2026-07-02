using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Base;

/// <summary>
/// Application 层轻量基类，只承载通用依赖，不提供业务 CRUD 模板。
/// </summary>
public abstract class ApplicationServiceBase(ServiceBaseContext context)
{
    protected readonly DbSession _session = context.Session;
    private readonly ILoggerFactory _loggerFactory = context.LoggerFactory;
    private ILogger? _loggerInstance;

    protected ILogger Logger => _loggerInstance ??= _loggerFactory.CreateLogger(GetType());
}

/// <summary>
/// 显式业务 Service 复用的 CRUD 组合对象。
/// 业务接口和 Controller 不再通过泛型 BaseService 暴露能力。
/// </summary>
internal sealed class ServiceCrud<TEntity, TDto>
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    private readonly IRepository<TEntity> _repository;
    private readonly DbSession _session;
    private readonly ILogger _logger;

    public ServiceCrud(IRepository<TEntity> repository, ServiceBaseContext context)
    {
        _repository = repository;
        _session = context.Session;
        _logger = context.LoggerFactory.CreateLogger($"ServiceCrud<{typeof(TEntity).Name}>");
    }

    private string EntityName => typeof(TEntity).Name;

    public async Task<bool> ExistsIdAsync(Guid uid) => await _repository.ExistsIdAsync(uid);

    public async Task<ServiceResult<IReadOnlyList<Guid>>> GetExistingIdListAsync(IEnumerable<Guid> uidList)
    {
        try
        {
            var result = await _repository.GetExistingIdListAsync(uidList);
            return result.Any()
                ? ServiceResult<IReadOnlyList<Guid>>.Ok(result)
                : ServiceResult<IReadOnlyList<Guid>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<Guid>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<TDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, EntityName);
                return ServiceResult<TDto>.Fail("未找到数据");
            }

            return ServiceResult<TDto>.Ok(entity.Adapt<TDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<TDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<Guid> uidList)
    {
        try
        {
            var entities = await _repository.GetByIdListAsync(uidList);
            return entities.Any()
                ? ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>())
                : ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<TDto?>> GetSingleEntityAsync()
    {
        try
        {
            var entity = await _repository.GetSingleEntityAsync();
            return entity == null
                ? ServiceResult<TDto?>.Fail("未找到数据")
                : ServiceResult<TDto?>.Ok(entity.Adapt<TDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<TDto?>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>())
                : ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null)
    {
        try
        {
            if (_repository is not IWhereQueryRepository<TEntity> queryRepository)
                return ServiceResult<IReadOnlyList<TDto>>.Fail($"{EntityName} 仓储未实现自定义条件查询");

            var entities = await queryRepository.GetListAsync(whereSql, param);
            return entities.Any()
                ? ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>())
                : ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria)
    {
        try
        {
            if (_repository is not ICriteriaQueryRepository<TEntity> queryRepository)
                return ServiceResult<IReadOnlyList<TDto>>.Fail($"{EntityName} 仓储未实现动态条件查询");

            var entities = await queryRepository.GetListAsync(criteria);
            return entities.Any()
                ? ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>())
                : ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object criteriaObject)
    {
        try
        {
            if (_repository is not IObjectQueryRepository<TEntity> queryRepository)
                return ServiceResult<IReadOnlyList<TDto>>.Fail($"{EntityName} 仓储未实现对象条件查询");

            var entities = await queryRepository.GetListAsync(criteriaObject);
            return entities.Any()
                ? ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>())
                : ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<TEntity> pagedRepository)
                return ServiceResult<PagedResultDto<TDto>>.Fail($"{EntityName} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<TDto>>.Ok(new PagedResultDto<TDto>
            {
                Items = paged.Items.Adapt<List<TDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<TDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(TDto dto)
    {
        try
        {
            var entity = dto.Adapt<TEntity>();
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

    public async Task<ServiceResult<int>> UpdateAsync(TDto dto)
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

    public async Task<ServiceResult<int>> DeleteSoftAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _repository.DeleteSoftAsync(uid));
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

    public async Task<ServiceResult<IReadOnlyList<Guid>>> AddRangeAsync(IEnumerable<TDto> dtoList)
    {
        try
        {
            if (_repository is not IBatchWriteRepository<TEntity> batchRepository)
                return ServiceResult<IReadOnlyList<Guid>>.Fail($"{EntityName} 仓储未实现批量新增");

            var entities = dtoList.Adapt<List<TEntity>>();
            return ServiceResult<IReadOnlyList<Guid>>.Ok(await batchRepository.AddRangeAsync(entities));
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<Guid>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<Guid> uidList)
    {
        try
        {
            if (_repository is not IBatchWriteRepository<TEntity> batchRepository)
                return ServiceResult<int>.Fail($"{EntityName} 仓储未实现批量删除");

            return ServiceResult<int>.Ok(await batchRepository.DeleteRangeAsync(uidList.ToList()));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<Guid> uidList)
    {
        try
        {
            if (_repository is not IBatchWriteRepository<TEntity> batchRepository)
                return ServiceResult<int>.Fail($"{EntityName} 仓储未实现批量逻辑删除");

            return ServiceResult<int>.Ok(await batchRepository.DeleteRangeSoftAsync(uidList.ToList()));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList)
    {
        try
        {
            if (_repository is not IBatchWriteRepository<TEntity> batchRepository)
                return ServiceResult<int>.Fail($"{EntityName} 仓储未实现批量修改");

            var entities = dtoList.Adapt<List<TEntity>>();
            return ServiceResult<int>.Ok(await batchRepository.UpdateRangeAsync(entities));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(
        BatchRequestDto<TDto> batchReq,
        Func<List<TEntity>, Task<string?>>? validateBeforeAddAsync = null,
        Func<List<TEntity>, Task<string?>>? validateBeforeUpdateAsync = null,
        Func<List<Guid>, Task<string?>>? validateBeforeDeleteAsync = null,
        bool isSoftDelete = true)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        var createList = batchReq.AddList?.Adapt<List<TEntity>>();
        var updateList = batchReq.UpdateList?.Adapt<List<TEntity>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (createList is { Count: > 0 } && validateBeforeAddAsync != null)
            {
                var error = await validateBeforeAddAsync(createList);
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

                if (validateBeforeUpdateAsync != null)
                {
                    var error = await validateBeforeUpdateAsync(updateList);
                    if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
                }
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");

                if (validateBeforeDeleteAsync != null)
                {
                    var error = await validateBeforeDeleteAsync(deleteIds);
                    if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
                }
            }

            if (errorMsg.Length > 0)
                return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<TEntity> batchRepository)
                return ServiceResult<BatchResultDto>.Fail($"{EntityName} 仓储未实现批量保存");

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
                if (isSoftDelete) await batchRepository.DeleteRangeSoftAsync(deleteIds);
                else await batchRepository.DeleteRangeAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<BatchResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<BatchResultDto>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
}
