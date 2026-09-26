using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Customers.CustomerProducts.Maintenance;
using SwiftExport.Application.Interfaces.Customers.CustomerProducts.Query;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Customers.CustomerProducts;

/// <summary>客户产品 HTTP 边界，分别委托联表查询和资料维护。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class CustomerProductController(ICustomerProductQueryService query, ICustomerProductMaintenanceService maintenance) : ControllerBase
{
    /// <summary>保留基础详情路由。</summary>
    [HttpGet("{id}")]
    public Task<ServiceResult<CustomerProductDto>> GetById(Guid id) => query.GetByIdAsync(id);

    /// <summary>保留基础新增路由与防重复标记。</summary>
    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<CustomerProductDto> dto) => maintenance.AddAsync(dto);

    /// <summary>保留基础更新路由与防重复标记。</summary>
    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<CustomerProductDto> dto) => maintenance.UpdateAsync(dto);

    /// <summary>保留基础删除路由与防重复标记。</summary>
    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id) => maintenance.DeleteAsync(id);

    /// <summary>联表分页查询。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPaged([FromBody] CustomerProductSearchRequest request) => query.GetPagedAsync(request);

    /// <summary>按客户料号读取。</summary>
    [HttpGet("customersku/{sku}")]
    public Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku) => query.GetByCustomerSkuAsync(sku, customerId);

    /// <summary>读取有产品资料的客户。</summary>
    [HttpGet("haveproductCustomers")]
    public Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerList() => query.GetAllHaveProductCustomerListAsync();

    /// <summary>原子保存客户产品变更集。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSave([FromBody] CustomerProductBatchSaveRequest request) => maintenance.BatchSaveAsync(request);
}
