using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Base;

public class BaseService<TEntity, TDto>(IRepository<TEntity> repository, ServiceBaseContext context) : IBaseService<TEntity, TDto>
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    protected readonly IRepository<TEntity> _repository = repository;
    protected readonly DbSession _session = context.Session;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());

    protected string EntityName => typeof(TEntity).Name;

    public virtual async Task<bool> ExistsIdAsync(int id)
    {
        return await _repository.ExistsIdAsync(id);
    }

    public virtual async Task<ServiceResult<IReadOnlyList<int>>> GetExistingIdListAsync(IEnumerable<int> idList)
    {
        try
        {
            var rst = await _repository.GetExistingIdListAsync(idList);
            if (!rst.Any()) return ServiceResult<IReadOnlyList<int>>.Fail("未找到数据");
            return ServiceResult<IReadOnlyList<int>>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<int>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<TDto>> GetByIdAsync(int id)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                // ★ 包裹 Warning 级别
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning("查询失败：未找到 ID 为 {Id} 的 {EntityName} 数据", id, EntityName);
                }
                return ServiceResult<TDto>.Fail("未找到数据");
            }

            return ServiceResult<TDto>.Ok(entity.Adapt<TDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<TDto>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<int> idList)
    {
        try
        {


            var entities = await _repository.GetByIdListAsync(idList);
            if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

            return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<TDto?>> GetSingleEntityAsync()
    {
        try
        {


            var entity = await _repository.GetSingleEntityAsync();
            return entity == null ? ServiceResult<TDto?>.Fail("未找到数据") : ServiceResult<TDto?>.Ok(entity.Adapt<TDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<TDto?>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync()
    {
        try
        {


            var entities = await _repository.GetAllAsync();
            if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

            return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null)
    {
        try
        {


            var entities = await _repository.GetListAsync(whereSql, param);
            if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

            return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria)
    {
        try
        {


            var entities = await _repository.GetListAsync(criteria);
            if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

            return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object noNameObj)
    {
        try
        {
            var entities = await _repository.GetListAsync(noNameObj);
            if (!entities.Any()) return ServiceResult<IReadOnlyList<TDto>>.Fail("未找到数据");

            return ServiceResult<IReadOnlyList<TDto>>.Ok(entities.Adapt<IReadOnlyList<TDto>>());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<TDto>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")
    {
        try
        {
            var entities = await _repository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);

            return ServiceResult<PagedResultDto<TDto>>.Ok(new PagedResultDto<TDto>
            {
                Items = entities.Items.Adapt<List<TDto>>(),
                TotalCount = entities.TotalCount,
                PageSize = entities.PageSize,
                PageIndex = entities.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<TDto>>.Fail(ex.Message);
        }

    }

    public virtual async Task<ServiceResult<int>> AddAsync(TDto dto)
    {
        try
        {
            var entity = dto.Adapt<TEntity>();

            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;

            var id = await _repository.AddAsync(entity);

            // ★ 包裹 Information 级别
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功新增 {EntityName} 数据，生成的 ID: {Id}", EntityName, id);
            }

            return ServiceResult<int>.Ok(id);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> UpdateAsync(TDto dto)
    {
        try
        {


            var existing = await _repository.GetByIdAsync(dto.Id);
            if (existing == null)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning("修改失败：未找到 ID 为 {Id} 的 {EntityName} 数据", dto.Id, EntityName);
                }
                return ServiceResult<int>.Fail("数据不存在");
            }

            dto.Adapt(existing);
            var rst = await _repository.UpdateAsync(existing);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功修改 {EntityName} 数据，ID: {Id}", EntityName, dto.Id);
            }

            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> DeleteSoftAsync(int id)
    {
        try
        {


            var rst = await _repository.DeleteSoftAsync(id);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功逻辑删除 {EntityName} 数据，ID: {Id}", EntityName, id);
            }
            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> DeleteAsync(int id)
    {
        try
        {


            var rst = await _repository.DeleteAsync(id);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功物理删除 {EntityName} 数据，ID: {Id}", EntityName, id);
            }
            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<IReadOnlyList<int>>> AddRangeAsync(IEnumerable<TDto> dtoList)
    {
        try
        {


            var entities = dtoList.Adapt<List<TEntity>>();

            var ids = await _repository.AddRangeAsync(entities);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功批量新增 {Count} 条 {EntityName} 数据", entities.Count, EntityName);
            }

            return ServiceResult<IReadOnlyList<int>>.Ok(ids);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<int>>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<int> idList)
    {
        try
        {
            var ids = idList.ToList();
            var rst = await _repository.DeleteRangeAsync(ids);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功批量物理删除 {Count} 条 {EntityName} 数据", ids.Count, EntityName);
            }
            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<int> idList)
    {
        try
        {
            var ids = idList.ToList();
            var rst = await _repository.DeleteRangeSoftAsync(ids);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功批量逻辑删除 {Count} 条 {EntityName} 数据", ids.Count, EntityName);
            }
            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public virtual async Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList)
    {
        try
        {

            var entities = dtoList.Adapt<List<TEntity>>();
            var rst = await _repository.UpdateRangeAsync(entities);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功批量修改 {Count} 条 {EntityName} 数据", entities.Count, EntityName);
            }
            return ServiceResult<int>.Ok(rst);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }




    #region  批量保存
    // ==========================================
    // 🚀 架构师留给子类的 "钩子 (Hooks)" 
    // ==========================================

    /// <summary>是否使用软删除？默认 true，子类可重写为 false</summary>
    protected virtual bool IsSoftDelete => true;

    /// <summary>新增前的特定业务校验，返回 null 代表校验通过，返回字符串代表报错信息</summary>
    protected virtual async Task<string?> ValidateBeforeAddAsync(List<TEntity> entities) => await Task.FromResult<string?>(null);

    /// <summary>修改前的特定业务校验</summary>
    protected virtual async Task<string?> ValidateBeforeUpdateAsync(List<TEntity> entities) => await Task.FromResult<string?>(null);

    /// <summary>删除前的特定业务校验</summary>
    protected virtual async Task<string?> ValidateBeforeDeleteAsync(List<int> ids) => await Task.FromResult<string?>(null);

    /// <summary>
    /// 批量保存,新增没写校验,更新写了防止Id为空的校验,删除没写校验
    /// </summary>
    /// <param name="batchReq"></param>
    /// <returns></returns>
    public virtual async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<TDto> batchReq)
    {
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation(
                "开始执行 [{EntityName}] 批量保存事务，计划新增: {AddCount}，修改: {UpdateCount}，删除: {DeleteCount}",
                EntityName, batchReq.AddList?.Count ?? 0, batchReq.UpdateList?.Count ?? 0, batchReq.DeleteIdList?.Count ?? 0);
        }

        // 1. 开启事务
        _session.Transaction = _session.Connection.BeginTransaction();
        int totalRowsAffected = 0;
        BatchResultDto batchResultDto = new();

        // 2. 映射实体
        var createList = batchReq.AddList?.Adapt<List<TEntity>>();
        var updateList = batchReq.UpdateList?.Adapt<List<TEntity>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            // =======================================================
            // 3. 执行校验 (基础校验 + 调用子类特定的钩子校验)
            // =======================================================
            var errorMsg = new StringBuilder();

            if (createList != null && createList.Count != 0)
            {
                // 调用子类特定的新增校验逻辑
                var customError = await ValidateBeforeAddAsync(createList);
                if (!string.IsNullOrEmpty(customError)) errorMsg.AppendLine(customError);
            }

            if (updateList != null && updateList.Count != 0)
            {
                // 基础的 ID 校验
                var invalidIds = updateList.Where(x => x.Id <= 0).Select(x => x.Id).ToList();
                if (invalidIds.Count != 0) errorMsg.AppendLine($"修改失败：包含无效ID {string.Join(",", invalidIds)}");

                // 检查数据库是否存在
                var needUpdateIds = updateList.Select(x => x.Id).ToList();
                var existingIds = await _repository.GetExistingIdListAsync(needUpdateIds);
                var nonExistentIds = needUpdateIds.Except(existingIds).ToList();
                if (nonExistentIds.Count != 0) errorMsg.AppendLine($"修改失败：以下ID在数据库中不存在 {string.Join(",", nonExistentIds)}");

                // 调用子类特定的修改校验逻辑
                var customError = await ValidateBeforeUpdateAsync(updateList);
                if (!string.IsNullOrEmpty(customError)) errorMsg.AppendLine(customError);
            }

            if (deleteIds != null && deleteIds.Count != 0)
            {
                var invalidIds = deleteIds.Where(id => id <= 0).ToList();
                if (invalidIds.Count != 0) errorMsg.AppendLine($"删除失败：包含无效ID {string.Join(",", invalidIds)}");

                // 调用子类特定的删除校验逻辑
                var customError = await ValidateBeforeDeleteAsync(deleteIds);
                if (!string.IsNullOrEmpty(customError)) errorMsg.AppendLine(customError);
            }

            // 如果任何校验失败，直接中断并返回错误
            if (errorMsg.Length > 0)
            {
                var finalError = errorMsg.ToString().TrimEnd();
                if (Logger.IsEnabled(LogLevel.Warning)) Logger.LogWarning("[{EntityName}] 批量保存中断（数据校验不通过）：{Error}", EntityName, finalError);
                return ServiceResult<BatchResultDto>.Fail(finalError);
            }

            // =======================================================
            // 4. 执行数据库写入 (UOW)
            // =======================================================
            if (createList != null && createList.Count > 0)
            {
                batchResultDto.AddIdList = [.. (await _repository.AddRangeAsync(createList))];
                totalRowsAffected += batchResultDto.AddIdList.Count;
            }

            if (updateList != null && updateList.Count > 0)
            {
                totalRowsAffected += await _repository.UpdateRangeAsync(updateList);
                batchResultDto.UpdateIdList = [.. updateList.Select(x => x.Id)];
            }

            if (deleteIds != null && deleteIds.Count > 0)
            {
                // 根据子类的开关，决定调用物理删除还是逻辑删除！
                if (IsSoftDelete)
                    totalRowsAffected += await _repository.DeleteRangeSoftAsync(deleteIds);
                else
                    totalRowsAffected += await _repository.DeleteRangeAsync(deleteIds);

                batchResultDto.DeleteIdList = deleteIds;
            }

            // 5. 提交事务
            _session.Transaction.Commit();

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("[{EntityName}] 批量保存事务提交！共影响 {Rows} 行。", EntityName, totalRowsAffected);
            }

            return ServiceResult<BatchResultDto>.Ok(batchResultDto);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            if (Logger.IsEnabled(LogLevel.Error)) Logger.LogError(ex, "[{EntityName}] 批量保存发生致命异常，事务已回滚！", EntityName);
            return ServiceResult<BatchResultDto>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    #endregion










}