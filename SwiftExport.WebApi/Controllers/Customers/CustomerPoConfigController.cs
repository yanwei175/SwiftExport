using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
namespace SwiftExport.WebApi.Controllers.Customers;

[ApiController]
[Route("api/[controller]")]
public class CustomerPoConfigController(ICustomerPoConfigService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<CustomerPoConfigDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] CustomerPoConfigDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] CustomerPoConfigDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSave([FromBody] CustomerPoConfigBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet]
    public async Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPaged([FromQuery] CustomerPoConfigSearchRequest request)
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

        return await service.GetPagedListAsync(criteria, pageIndex, pageSize, "CustomerCode ASC, Id DESC");
    }

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }

}

