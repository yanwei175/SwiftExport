using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.Catalog.DefaultSupplier;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Maintenance;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Query;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Suppliers.SupplierProducts;

/// <summary>供应商产品 HTTP 边界；默认供应商操作委托 Product 职责。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SupplierProductController(
    ISupplierProductQueryService query,
    ISupplierProductMaintenanceService maintenance,
    IProductDefaultSupplierService defaultSupplier) : ControllerBase
{
    /// <summary>保留基础详情路由。</summary>
    [HttpGet("{id}")]
    public Task<ServiceResult<SupplierProductDto>> GetById(Guid id) => query.GetByIdAsync(id);

    /// <summary>新增供应商产品资料。</summary>
    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<SupplierProductDto> dto) => maintenance.AddAsync(dto);

    /// <summary>更新供应商产品资料。</summary>
    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<SupplierProductDto> dto) => maintenance.UpdateAsync(dto);

    /// <summary>删除供应商产品资料。</summary>
    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id) => maintenance.DeleteAsync(id);

    /// <summary>按供应商分页读取资料。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPaged([FromBody] SupplierProductSearchRequest request) => query.GetPagedAsync(request);

    /// <summary>原子保存资料变更。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSave([FromBody] SupplierProductBatchSaveRequest request) => maintenance.BatchSaveAsync(request);

    /// <summary>按原路由设置指定 Product 的默认供应商。</summary>
    [HttpPost("set-product-default-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplier([FromBody] SetProductDefaultSupplierCommand command) =>
        defaultSupplier.SetProductDefaultSupplierAsync(command);
}
