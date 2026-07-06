using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
namespace SwiftExport.WebApi.Controllers.Suppliers;

[ApiController]
[Route("api/[controller]")]
public class SupplierController(ISupplierService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<SupplierDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] SupplierDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SupplierDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SupplierBatchSaveResponse>> BatchSave([FromBody] SupplierBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet]
    public async Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPaged([FromQuery] SupplierSearchRequest request)
    {

        // 1. 实例化底层查询对象
        var criteria = new QueryCriteria();

        // ==========================================
        // 处理批量搜索
        // ==========================================
        if (request.BatchKeywords != null && request.BatchKeywords.Count > 0)
        {


            foreach (var keyword in request.BatchKeywords)
            {
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("SupplierCode", 查询操作符.包含某值, keyword),
                    ("Name", 查询操作符.包含某值, keyword)
                );
            }
        }

        // 4. 处理分页默认值保护
        int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        int pageSize = request.PageSize > 0 ? request.PageSize : 50;

        return await service.GetPagedListAsync(criteria, pageIndex, pageSize, "SupplierCode ASC");
    }

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }


}
