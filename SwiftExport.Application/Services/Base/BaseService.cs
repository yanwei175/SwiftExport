using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Services.Base;

/// <summary>提供单实体 CRUD 编排；业务校验返回阻断，技术异常记录日志并返回统一错误。</summary>
public class BaseService<TEntity, TDto>(IRepository<TEntity> repository, ServiceBaseContext context) : IBaseService<TEntity, TDto> where TEntity : BaseEntity where TDto : BaseDto
{
    protected readonly IRepository<TEntity> _repository = repository;
    protected readonly IDbSession _session = context.Session;
    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());
    protected string EntityName => typeof(TEntity).Name;
    protected virtual async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<TDto>> createItems) => await Task.FromResult<string?>(null);
    /// <summary>校验新增请求并写入实体，返回保存结果或结构化失败。</summary>
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
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SaveItemResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }
    protected virtual async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<TDto>> updateItems) => await Task.FromResult<string?>(null);
    /// <summary>校验更新并保存目标实体，目标缺失返回未找到错误。</summary>
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
                return ServiceResult<SaveItemResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.NotFound, "数据不存在");
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
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SaveItemResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }
    protected virtual async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> deleteItems) => await Task.FromResult<string?>(null);
    /// <summary>校验删除条件并按版本删除，失败保留业务或技术错误分类。</summary>
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
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SaveItemResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }
    /// <summary>读取指定实体并转换为 DTO，缺失和技术错误分别返回。</summary>
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
                return ServiceResult<TDto>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.NotFound, "未找到数据");
            }

            return ServiceResult<TDto>.Ok(entity.Adapt<TDto>());
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<TDto>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }





}
