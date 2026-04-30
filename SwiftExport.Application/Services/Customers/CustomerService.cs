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

public class CustomerService(ICustomerRepository repository, ServiceBaseContext context)
    : BaseService<Customer, CustomerDto>(repository, context), ICustomerService
{
    #region 批量保存
    protected override bool IsSoftDelete => false;
    protected override async Task<string?> ValidateBeforeAddAsync(List<Customer> entities)
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

        // 2. 调用当前业务专用的 Repository 方法查重
        // 注意：因为基类的 _rpRepository 是泛型 IRepository<T>，这里需要向下转型为 ICustomerRepository
        var repo = (ICustomerRepository)_repository;
        var existedCodes = await repo.GetExistingCustomerCodesAsync(needAddCodes);

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
        return await repository.ExistByCustomerCode(customerCode);
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

        var list = await repository.GetListAsync("WHERE CustomerCode = @CustomerCode", new { CustomerCode = customerCode });
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

        var existingCodes = await repository.GetExistingCustomerCodesAsync(customerCodes);

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
        if (!string.IsNullOrWhiteSpace(request.BatchSearchTexts))
        {
            // 1. 分割字符串：支持 换行符(\n, \r) 和 逗号(,)
            var batchList = request.BatchSearchTexts
                .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())           // 去除首尾空格
                .Where(k => !string.IsNullOrEmpty(k)) // 再次确保没有空字符串
                .Distinct()                      // 去重
                .ToList();

            if (batchList.Count > 0)
            {
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("CustomerCode", 查询操作符.包含某值, batchList),
                    ("CustomerName", 查询操作符.包含某值, batchList),
                    ("ContactPerson", 查询操作符.包含某值, batchList)
                );
            }
        }

        // ==========================================
        // 处理单个关键字模糊搜索
        // ==========================================
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            criteria.AddOrGroup(
                ("CustomerCode", 查询操作符.模糊匹配, request.SearchText),
                ("CustomerName", 查询操作符.模糊匹配, request.SearchText),
                ("ContactPerson", 查询操作符.模糊匹配, request.SearchText)
            );
        }

        // 4. 处理分页默认值保护
        int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        int pageSize = request.PageSize > 0 ? request.PageSize : 50;

        // 5. 核心：调用基类提供的泛型分页方法，完成数据库查询和 DTO 映射
        return await base.GetPagedListAsync(criteria, pageIndex, pageSize, "CustomerCode ASC");
    }
}