using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.WebApi.Controllers.Base;
namespace SwiftExport.WebApi.Controllers.Customers;

public class CustomerPoConfigController(ICustomerPoConfigService service) : BaseController<CustomerPoConfig, CustomerPoConfigDto>(service)
{
    [HttpGet]
    public async Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPaged([FromQuery] PagedRequestDto request)
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
                // 场景 A：只匹配 SKU (性能最高，最常用)
                // criteria.Add("SKU", 查询操作符.包含某值, batchList);

                // 场景 B：匹配 SKU 或 客户型号 (更灵活)
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("CustomerCode", 查询操作符.包含某值, keyword),
                    ("SalesPerson", 查询操作符.包含某值, keyword),
                    ("Purchaser", 查询操作符.包含某值, keyword),
                    ("SupplierCode", 查询操作符.包含某值, keyword)
                );
            }
        }


        // 4. 处理分页默认值保护
        int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        int pageSize = request.PageSize > 0 ? request.PageSize : 50;

        // 5. 核心：调用基类提供的泛型分页方法，完成数据库查询和 DTO 映射
        return await _service.GetPagedListAsync(criteria, pageIndex, pageSize, "CustomerCode ASC, Id DESC");
    }

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }

}

