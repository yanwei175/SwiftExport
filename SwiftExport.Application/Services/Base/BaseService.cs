using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Base;

public class BaseService<TEntity, TDto>(IRepository<TEntity> repository, ServiceBaseContext context) : IBaseService<TEntity, TDto> where TEntity : BaseEntity where TDto : BaseDto
{
    protected readonly IRepository<TEntity> _repository = repository;
    protected readonly DbSession _session = context.Session;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());
    protected string EntityName => typeof(TEntity).Name;
    protected virtual async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<TDto>> createItems) => await Task.FromResult<string?>(null);
    public virtual async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<TDto> item)
    {
        try
        {
            var errorMsg = await ValidateBeforeCreateAsync([item]);
            if (!string.IsNullOrEmpty(errorMsg))
            {
                var finalError = errorMsg.ToString().TrimEnd();
                if (Logger.IsEnabled(LogLevel.Warning))
                    Logger.LogWarning("[{EntityName}] 新增不成功（数据校验不通过）：{Error}", EntityName, finalError);
                return ServiceResult<SaveItemResult>.Fail(finalError);
            }

            var entity = item.Data.Adapt<TEntity>();

            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;

            var writeResult = await _repository.AddAsync(entity);

            // ★ 包裹 Information 级别
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功新增 {EntityName} 数据，生成的 Uid: {Uid}", EntityName, writeResult);
            }

            return ServiceResult<SaveItemResult>.Ok(SaveItemResultFactory.FromCreate(item, writeResult));
        }
        catch (Exception ex)
        {
            return ServiceResult<SaveItemResult>.Fail(ex.Message);
        }
    }
    protected virtual async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<TDto>> updateItems) => await Task.FromResult<string?>(null);
    public virtual async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<TDto> item)
    {
        try
        {
            var errorMsg = await ValidateBeforeUpdateAsync([item]);
            if (!string.IsNullOrEmpty(errorMsg))
            {
                var finalError = errorMsg.ToString().TrimEnd();
                if (Logger.IsEnabled(LogLevel.Warning))
                    Logger.LogWarning("[{EntityName}] 修改不成功（数据校验不通过）：{Error}", EntityName, finalError);
                return ServiceResult<SaveItemResult>.Fail(finalError);
            }


            var existing = await _repository.GetByIdAsync(item.Uid);
            if (existing == null)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning("修改失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", item.Uid, EntityName);
                }
                return ServiceResult<SaveItemResult>.Fail("数据不存在");
            }

            item.Data.Adapt(existing);
            var writeResult = await _repository.UpdateAsync(existing);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功修改 {EntityName} 数据，Uid: {Uid}", EntityName, item.Uid);
            }

            return ServiceResult<SaveItemResult>.Ok(SaveItemResultFactory.FromUpdate(item, writeResult));
        }
        catch (Exception ex)
        {
            return ServiceResult<SaveItemResult>.Fail(ex.Message);
        }
    }
    protected virtual async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> deleteItems) => await Task.FromResult<string?>(null);
    public virtual async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
    {
        try
        {
            var errorMsg = await ValidateBeforeDeleteAsync([item]);
            if (!string.IsNullOrEmpty(errorMsg))
            {
                var finalError = errorMsg.ToString().TrimEnd();
                if (Logger.IsEnabled(LogLevel.Warning))
                    Logger.LogWarning("[{EntityName}] 删除不成功（数据校验不通过）：{Error}", EntityName, finalError);
                return ServiceResult<SaveItemResult>.Fail(finalError);
            }

            var writeResult = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("成功软删除 {EntityName} 数据，Uid: {Uid}", EntityName, item.Uid);
            }
            return ServiceResult<SaveItemResult>.Ok(SaveItemResultFactory.FromDelete(item, writeResult));
        }
        catch (Exception ex)
        {
            return ServiceResult<SaveItemResult>.Fail(ex.Message);
        }
    }
    public virtual async Task<ServiceResult<TDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                // ★ 包裹 Warning 级别
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, EntityName);
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





}
