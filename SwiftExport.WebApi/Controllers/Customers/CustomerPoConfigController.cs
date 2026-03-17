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
                // 场景 A：只匹配 SKU (性能最高，最常用)
                // criteria.Add("SKU", 查询操作符.包含某值, batchList);

                // 场景 B：匹配 SKU 或 客户型号 (更灵活)
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("CustomerCode", 查询操作符.包含某值, batchList),
                    ("SalesPerson", 查询操作符.包含某值, batchList),
                    ("Purchaser", 查询操作符.包含某值, batchList),
                    ("SupplierCode", 查询操作符.包含某值, batchList)
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
                ("SalesPerson", 查询操作符.模糊匹配, request.SearchText),
                ("Purchaser", 查询操作符.模糊匹配, request.SearchText),
                ("SupplierCode", 查询操作符.模糊匹配, request.SearchText)
            );
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


    [HttpPost("batch-save")]
    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync([FromBody] BatchRequestGeneric<CustomerPoConfigDto> request)
    {
        return await service.BatchSaveAsync(request);
    }
}

