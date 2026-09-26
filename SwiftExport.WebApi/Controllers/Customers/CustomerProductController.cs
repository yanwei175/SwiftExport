using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Customers;

/// <summary>
/// 客户产品 API。
/// 基础 CRUD 复用 BaseController；联表分页和批量保存端点在此显式定义。
/// </summary>
public class CustomerProductController(ICustomerProductService service) : BaseController<CustomerProduct, CustomerProductDto>(service)
{
    // ================= 读操作 =================
    /// <summary>联表分页搜索</summary>
    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPaged([FromBody] CustomerProductSearchRequest request)
        => await service.GetPagedAsync(request);

    /// <summary>按客户料号查询</summary>
    [HttpGet("customersku/{sku}")]
    public async Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku)
        => await service.GetByCustomerSkuAsync(sku, customerId);

    /// <summary>获取有产品记录的客户列表</summary>
    [HttpGet("haveproductCustomers")]
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerList()
        => await service.GetAllHaveProductCustomerListAsync();

    // ================= 写操作 =================

    /// <summary>批量保存（新增 / 修改 / 删除），事务保护</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSave([FromBody] CustomerProductBatchSaveRequest request)
        => await service.BatchSaveAsync(request);
}
