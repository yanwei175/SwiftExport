using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Customers;

public partial class CustomerService(ICustomerRepository repository, ServiceBaseContext context)
    : ApplicationServiceBase(context), ICustomerService
{
    private readonly ServiceCrud<Customer, CustomerDto> _crud = new(repository, context);
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
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning("查询失败：传入的客户代码为空");
            }
            return ServiceResult<CustomerDto>.Fail("客户代码不能为空");
        }

        var list = await _repository.GetListAsync("WHERE CustomerCode = @CustomerCode", new { CustomerCode = customerCode });
        if (list == null || list.Count == 0)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning("查询失败：未找到 CustomerCode 为 {CustomerCode} 的客户", customerCode);
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
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning("提取未存在客户代码失败：列表中包含空值");
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

    public async Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedAsync(PagedRequestDto request)
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

        return await _crud.GetPagedListAsync(criteria, pageIndex, pageSize, "CustomerCode ASC");
    }
}
