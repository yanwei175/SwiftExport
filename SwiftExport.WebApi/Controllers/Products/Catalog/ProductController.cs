using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;
using SwiftExport.Application.Interfaces.Products.Catalog.Query;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.Catalog;

/// <summary>产品 HTTP 边界，分别委托只读查询和资料维护服务。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductController(IProductQueryService query, IProductMaintenanceService maintenance) : ControllerBase
{
    /// <summary>保留基础详情路由。</summary>
    [HttpGet("{id}")]
    public Task<ServiceResult<ProductDto>> GetById(Guid id) => query.GetByIdAsync(id);

    /// <summary>保留基础新增路由与防重复标记。</summary>
    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<ProductDto> dto) => maintenance.AddAsync(dto);

    /// <summary>保留基础更新路由与防重复标记。</summary>
    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<ProductDto> dto) => maintenance.UpdateAsync(dto);

    /// <summary>保留基础删除路由与防重复标记。</summary>
    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id) => maintenance.DeleteAsync(id);

    /// <summary>读取产品分页。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPaged([FromBody] ProductSearchRequest request) => query.GetPagedAsync(request);

    /// <summary>搜索产品选择器。</summary>
    [HttpPost("lookup")]
    public Task<ServiceResult<List<ProductLookupDto>>> SearchLookup([FromBody] ProductLookupRequest request) => query.SearchLookupAsync(request);

    /// <summary>保留大数据量查询阻断。</summary>
    [HttpGet("all")]
    public Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAll() =>
        Task.FromResult(ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll"));

    /// <summary>读取产品关联客户编码。</summary>
    [HttpGet("customerCodes")]
    public Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeList() => query.GetAllCustomerCodeListAsync();

    /// <summary>按 SKU 读取产品。</summary>
    [HttpGet("sku/{sku}")]
    public Task<ServiceResult<ProductDto>> GetBySku(string sku) => query.GetBySkuAsync(sku);

    /// <summary>原子保存产品变更集。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductBatchSaveResponse>> BatchSave([FromBody] ProductBatchSaveRequest request) => maintenance.BatchSaveAsync(request);

    /// <summary>产品维护专用的活动仓库参考候选；不开放库存模块的仓库查询。</summary>
    [Authorize]
    [HttpGet("default-warehouse-candidates")]
    public Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidates(
        [FromQuery] PagedRequestBase request, CancellationToken cancellationToken) =>
        query.GetDefaultWarehouseCandidatesAsync(request, cancellationToken);
}
