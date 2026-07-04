using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Customers;

public partial class CustomerPoConfigService(ICustomerPoConfigRepository repository, ServiceBaseContext context) : ICustomerPoConfigService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<CustomerPoConfigService> _logger = context.LoggerFactory.CreateLogger<CustomerPoConfigService>();
    private readonly ICustomerPoConfigRepository _repository = repository;

    #region 显式基础读写
    public async Task<ServiceResult<CustomerPoConfigDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(CustomerPoConfig));
                return ServiceResult<CustomerPoConfigDto>.Fail("未找到数据");
            }

            return ServiceResult<CustomerPoConfigDto>.Ok(entity.Adapt<CustomerPoConfigDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<CustomerPoConfigDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Ok(entities.Adapt<IReadOnlyList<CustomerPoConfigDto>>())
                : ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<CustomerPoConfig> pagedRepository)
                return ServiceResult<PagedResultDto<CustomerPoConfigDto>>.Fail($"{nameof(CustomerPoConfig)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<CustomerPoConfigDto>>.Ok(new PagedResultDto<CustomerPoConfigDto>
            {
                Items = paged.Items.Adapt<List<CustomerPoConfigDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<CustomerPoConfigDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(CustomerPoConfigDto dto)
    {
        try
        {
            var entity = dto.Adapt<CustomerPoConfig>();
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

    public async Task<ServiceResult<int>> UpdateAsync(CustomerPoConfigDto dto)
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

    #endregion

    #region 批量保存

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerPoConfigDto> batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        var createList = batchReq.AddList?.Adapt<List<CustomerPoConfig>>();
        var updateList = batchReq.UpdateList?.Adapt<List<CustomerPoConfig>>();
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

            if (_repository is not IBatchWriteRepository<CustomerPoConfig> batchRepository)
                return ServiceResult<BatchResultDto>.Fail($"{nameof(CustomerPoConfig)} 仓储未实现批量保存");

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

    private async Task<string?> ValidateBeforeAddAsync(List<CustomerPoConfig> entities)
    {
        // A. 自身列表内的查重 (Self-Duplicates)
        var selfDuplicates = entities
            .GroupBy(x => new { x.CustomerCode, x.SupplierCode })
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.CustomerCode}|{g.Key.SupplierCode}")
            .ToList();

        if (selfDuplicates.Count > 0)
        {
            // 如果自身都有重复，这批新增数据直接打回
            return "批量保存失败：新增列表中存在重复的 客户+供应商 组合！";
        }

        var needAdd = new List<PoConfigCheck>();

        // B. 必填项空值检查
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.CustomerCode)) return "客户代码不能为空";
            if (string.IsNullOrWhiteSpace(item.SupplierCode)) return "供应商代码不能为空";

            needAdd.Add(new PoConfigCheck { CustomerCode = item.CustomerCode, SupplierCode = item.SupplierCode });
        }

        var nonUniquePoConfig = await _repository.GetNonUniqueByPoConfigCheckList(needAdd);

        if (nonUniquePoConfig.Count > 0)
        {
            var wrongAdds = nonUniquePoConfig.Select(x => $"{x.CustomerCode}|{x.SupplierCode}");
            return $"新创建的在数据库中已存在，拒绝新增。重复组合: {string.Join(", ", wrongAdds)}";
        }

        // 一切正常，放行给基类写入！
        return null;
    }

    #endregion

}
