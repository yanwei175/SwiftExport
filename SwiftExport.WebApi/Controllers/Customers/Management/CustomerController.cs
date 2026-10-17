using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Customers.Management;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Customers.Management;

/// <summary>客户管理 HTTP 入口，查询和写入规则委托应用服务。</summary>
public class CustomerController(ICustomerService service) : BaseController<Customer, CustomerDto>(service)
{

    /// <summary>读取客户管理完整快照；权限由统一端点过滤器执行。</summary>
    [HttpGet("all")]
    public Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAll(CancellationToken cancellationToken) =>
        service.GetAllAsync(cancellationToken);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPaged([FromBody] CustomerSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 2)]
    public async Task<ServiceResult<CustomerBatchSaveResponse>> BatchSave([FromBody] CustomerBatchSaveRequest req) => await service.BatchSaveAsync(req);


}
