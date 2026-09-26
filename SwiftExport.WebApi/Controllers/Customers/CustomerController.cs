using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Customers;

public class CustomerController(ICustomerService service) : BaseController<Customer, CustomerDto>(service)
{

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPaged([FromBody] CustomerSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<CustomerBatchSaveResponse>> BatchSave([FromBody] CustomerBatchSaveRequest req) => await service.BatchSaveAsync(req);


}
