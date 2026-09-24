using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Customers;


public class CustomerPoConfigController(ICustomerPoConfigService service) : BaseController<CustomerPoConfig, CustomerPoConfigDto>(service)
{
    /// <summary>按客户与供应商标识查询完整的只读采购默认配置。</summary>
    [HttpGet("by-parties")]
    public async Task<ServiceResult<CustomerPoConfigDto>> GetByParties(
        [FromQuery] Guid customerId,
        [FromQuery] Guid supplierId,
        CancellationToken cancellationToken)
    {
        return await service.GetByPartiesAsync(customerId, supplierId, cancellationToken);
    }

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPaged([FromBody] CustomerPoConfigSearchRequest request)
    {
        return await service.GetPagedAsync(request);
    }

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSave([FromBody] CustomerPoConfigBatchSaveRequest req) => await service.BatchSaveAsync(req);

}

