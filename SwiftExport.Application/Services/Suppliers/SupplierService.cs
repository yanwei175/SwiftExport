using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Suppliers;

public partial class SupplierService(ISupplierRepository repository, ServiceBaseContext context) : ISupplierService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SupplierService> _logger = context.LoggerFactory.CreateLogger<SupplierService>();
    private readonly ISupplierRepository _repository = repository;

    #region 批量保存
    /// <summary>
    /// 批量保存
    /// </summary>
    private async Task<string?> ValidateBeforeAddAsync(List<Supplier> entities)
    {
        var needAddCodes = new List<string>();

        // A. 校验必填项
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.SupplierCode))
            {
                return "供应商代码不能为空"; // 遇到致命错误直接返回提示，基类会中断并回滚
            }
            needAddCodes.Add(item.SupplierCode);
        }

        var existedCodes = await _repository.GetExistingSupplierCodesAsync(needAddCodes);

        // C. 组装特定报错信息
        if (existedCodes.Any())
        {
            return $"新创建的供应商代码在数据库中已存在，拒绝新增。重复代码: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切校验完美通过，放行让基类去写数据库！
        return null;
    }
    #endregion








    #region 显式基础读写

    public async Task<ServiceResult<SupplierDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(Supplier));
                return ServiceResult<SupplierDto>.Fail("未找到数据");
            }

            return ServiceResult<SupplierDto>.Ok(entity.Adapt<SupplierDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<SupplierDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SupplierDto>>.Ok(entities.Adapt<IReadOnlyList<SupplierDto>>())
                : ServiceResult<IReadOnlyList<SupplierDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SupplierDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<Supplier> pagedRepository)
                return ServiceResult<PagedResultDto<SupplierDto>>.Fail($"{nameof(Supplier)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<SupplierDto>>.Ok(new PagedResultDto<SupplierDto>
            {
                Items = paged.Items.Adapt<List<SupplierDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<SupplierDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(SupplierDto dto)
    {
        try
        {
            var entity = dto.Adapt<Supplier>();
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

    public async Task<ServiceResult<int>> UpdateAsync(SupplierDto dto)
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

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SupplierDto> batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        var createList = batchReq.AddList?.Adapt<List<Supplier>>();
        var updateList = batchReq.UpdateList?.Adapt<List<Supplier>>();
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
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<Supplier> batchRepository)
                return ServiceResult<BatchResultDto>.Fail($"{nameof(Supplier)} 仓储未实现批量保存");

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
                await batchRepository.DeleteRangeAsync(deleteIds);
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
    #endregion

}

