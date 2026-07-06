using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Infrastructure.Data;
using System.Text;

using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
namespace SwiftExport.Application.Services.Customers;

public partial class CustomerService(ICustomerRepository repository, ServiceBaseContext context) : ICustomerService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<CustomerService> _logger = context.LoggerFactory.CreateLogger<CustomerService>();
    private readonly ICustomerRepository _repository = repository;

    #region 批量保存
    private async Task<string?> ValidateBeforeAddAsync(List<Customer> entities)
    {
        var needAddCodes = new List<string>();

        // 1. 校验必填项
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.CustomerCode))
            {
                return "客户代码不能为空"; // 遇到致命错误直接返回提示
            }
            needAddCodes.Add(item.CustomerCode);
        }

        var existedCodes = await _repository.GetExistingCustomerCodesAsync(needAddCodes);

        // 3. 组装特定报错信息
        if (existedCodes.Any())
        {
            return $"新创建的客户代码在数据库中已存在，拒绝新增。重复代码: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切校验完美通过，放行让基类去写数据库！
        return null;
    }
    #endregion

    public async Task<bool> ExistByCustomerCode(string customerCode)
    {
        return await _repository.ExistByCustomerCode(customerCode);
    }

    public async Task<ServiceResult<CustomerDto>> GetByCustomerCodeAsync(string customerCode)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("查询失败：传入的客户代码为空");
            }
            return ServiceResult<CustomerDto>.Fail("客户代码不能为空");
        }

        var list = await _repository.GetListAsync("WHERE CustomerCode = @CustomerCode", new { CustomerCode = customerCode });
        if (list == null || list.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("查询失败：未找到 CustomerCode 为 {CustomerCode} 的客户", customerCode);
            }
            return ServiceResult<CustomerDto>.Fail($"未找到代码为 {customerCode} 的客户");
        }

        var entity = list[0];
        return ServiceResult<CustomerDto>.Ok(entity.Adapt<CustomerDto>());
    }

    public async Task<ServiceResult<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
    {
        if (customerCodes == null || !customerCodes.Any())
        {
            return ServiceResult<IReadOnlyList<string>>.Fail("客户代码列表不能为空");
        }

        if (customerCodes.Any(code => string.IsNullOrWhiteSpace(code)))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("提取未存在客户代码失败：列表中包含空值");
            }
            return ServiceResult<IReadOnlyList<string>>.Fail("客户代码列表中包含空值。");
        }

        var existingCodes = await _repository.GetExistingCustomerCodesAsync(customerCodes);

        if (existingCodes == null || !existingCodes.Any())
            return ServiceResult<IReadOnlyList<string>>.Ok([.. customerCodes]);

        var allCodesSet = new HashSet<string>(customerCodes, StringComparer.OrdinalIgnoreCase);
        var existingCodesSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var nonExistentCodes = allCodesSet.Except(existingCodesSet).ToList();

        return ServiceResult<IReadOnlyList<string>>.Ok(nonExistentCodes);
    }

    public async Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request)
    {

        // 1. 实例化底层查询对象
        var criteria = new QueryCriteria();

        // ==========================================
        // 处理批量搜索
        // ==========================================
        if (request.BatchKeywords!=null && request.BatchKeywords.Count>0)
        {

            foreach (var keyword in request.BatchKeywords) 
            {
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("CustomerCode", 查询操作符.包含某值, keyword),
                    ("CustomerName", 查询操作符.包含某值, keyword),
                    ("ContactPerson", 查询操作符.包含某值, keyword)
                );
            }
        }

        // 4. 处理分页默认值保护
        int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        int pageSize = request.PageSize > 0 ? request.PageSize : 50;

        return await GetPagedListAsync(criteria, pageIndex, pageSize, "CustomerCode ASC");
    }


    #region 显式基础读写

    public async Task<ServiceResult<CustomerDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(Customer));
                return ServiceResult<CustomerDto>.Fail("未找到数据");
            }

            return ServiceResult<CustomerDto>.Ok(entity.Adapt<CustomerDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<CustomerDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<CustomerDto>>.Ok(entities.Adapt<IReadOnlyList<CustomerDto>>())
                : ServiceResult<IReadOnlyList<CustomerDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_repository is not IPagedCriteriaRepository<Customer> pagedRepository)
                return ServiceResult<PagedResultDto<CustomerDto>>.Fail($"{nameof(Customer)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<CustomerDto>>.Ok(new PagedResultDto<CustomerDto>
            {
                Items = paged.Items.Adapt<List<CustomerDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<CustomerDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(CustomerDto dto)
    {
        try
        {
            var entity = dto.Adapt<Customer>();
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

    public async Task<ServiceResult<int>> UpdateAsync(CustomerDto dto)
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

    public async Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new CustomerBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<Customer>>();
        var updateList = batchReq.UpdateList?.Adapt<List<Customer>>();
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
                return ServiceResult<CustomerBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<Customer> batchRepository)
                return ServiceResult<CustomerBatchSaveResponse>.Fail($"{nameof(Customer)} 仓储未实现批量保存");

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
            return ServiceResult<CustomerBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<CustomerBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}
