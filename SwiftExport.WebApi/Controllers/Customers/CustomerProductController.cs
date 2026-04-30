using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Customers;

/// <summary>
/// 客户产品 API。
/// 因 CustomerProductDto 含联表字段，不继承 BaseController 泛型，独立定义端点。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CustomerProductController(ICustomerProductService service) : ControllerBase
{
    // ================= 读操作 =================

    /// <summary>按 ID 查询单条客户产品（含联表字段）</summary>
    [HttpGet("{id:int}")]
    public async Task<ServiceResult<CustomerProductDto>> GetById(int id)
        => await service.GetByIdAsync(id);

    /// <summary>按客户料号查询</summary>
    [HttpGet("customersku/{sku}")]
    public async Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(int customerId, string sku)
        => await service.GetByCustomerSkuAsync(sku, customerId);

    /// <summary>联表分页搜索</summary>
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<CustomerProductDto>>> Search([FromBody] ProductSearchDto search)
        => await service.GetPagedCustomerProductAsync(search);

    /// <summary>获取有产品记录的客户列表</summary>
    [HttpGet("haveproductCustomers")]
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerList()
        => await service.GetAllHaveProductCustomerListAsync();

    // ================= 写操作 =================

    /// <summary>批量保存（新增 / 修改 / 删除），事务保护</summary>
    [HttpPost("batch-save")]
    public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestGeneric<CustomerProductDto> req)
        => await service.BatchSaveAsync(req);
}
