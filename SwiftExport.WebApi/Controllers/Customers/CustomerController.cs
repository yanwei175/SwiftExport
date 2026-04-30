using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Controllers.Base;
namespace SwiftExport.WebApi.Controllers.Customers;

public class CustomerController(ICustomerService service) : BaseController<SwiftExport.Core.Entities.MasterData.Customer, CustomerDto>(service)
{
    [HttpGet("page")]
    public async Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPaged([FromQuery] PagedRequestDto request)
    {
        return await service.GetPagedAsync(request);
    }

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAll()
    {
        return await _service.GetAllAsync();
    }

}