using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;
using SwiftExport.Application.Interfaces.Products.Catalog.Query;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.Catalog;

/// <summary>产品 HTTP 边界，分别委托只读查询和资料维护服务。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductController(IProductQueryService query, IProductMaintenanceService maintenance,
    IProductMasterMaintenanceService masterMaintenance) : ControllerBase
{
    /// <summary>读取物料编辑详情与自身标签，来源模板由服务端解析。</summary>
    [HttpGet("{id}/detail")]
    public Task<ServiceResult<ProductDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken) => query.GetDetailAsync(id, cancellationToken);

    /// <summary>按物料预期版本保存可写资料和自身标签；路径与请求身份必须一致。</summary>
    [HttpPost("{id}/detail")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductDetailResponse>> SaveDetail(Guid id, [FromBody] SaveProductDetailRequest request, CancellationToken cancellationToken)
    {
        if (request is null || id != request.ProductId)
            return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.Validation, "路径 Id 与请求物料身份不一致。", "ProductId"));
        return maintenance.SaveDetailAsync(request, cancellationToken);
    }
    /// <summary>保留基础详情路由。</summary>
    [HttpGet("{id}")]
    public Task<ServiceResult<ProductDto>> GetById(Guid id) => query.GetByIdAsync(id);

    /// <summary>保留基础新增路由与防重复标记。</summary>
    [HttpPost]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<ProductDto> dto) => maintenance.AddAsync(dto);

    /// <summary>核对路径与外层请求身份后更新资料，身份冲突不调用写入服务。</summary>
    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<ProductDto> dto)
    {
        if (id != dto.Uid)
            return Task.FromResult(ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.Validation, "路径 Id 与请求体 Uid 不一致。", "Uid"));
        return maintenance.UpdateAsync(dto);
    }

    /// <summary>保留基础删除路由与防重复标记。</summary>
    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id) => maintenance.DeleteAsync(id);

    /// <summary>读取产品分页。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPaged([FromBody] ProductSearchRequest request) => query.GetPagedAsync(request);

    /// <summary>搜索产品选择器。</summary>
    [HttpPost("lookup")]
    public Task<ServiceResult<List<ProductLookupDto>>> SearchLookup([FromBody] ProductLookupRequest request, CancellationToken cancellationToken) => query.SearchLookupAsync(request, cancellationToken);

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

    /// <summary>按版本只更新 BOM 绑定，服务端复核共享结构并保留其他产品字段。</summary>
    [HttpPost("{id}/bom-binding")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveItemResult>> BindBom(Guid id, [FromBody] ProductBomBindingRequest request,
        [FromServices] SwiftExport.Application.Interfaces.Products.BomManagement.IProductBomBindingService binding,
        CancellationToken cancellationToken) => binding.BindAsync(id, request, cancellationToken);

    /// <summary>按产品编辑权限从所选产品的主型号拉取中英文描述。</summary>
    [HttpPost("pull-master-descriptions")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterDescriptionsAsync([FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken)
        => masterMaintenance.PullProductsAsync(request, ProductMasterSyncFields.Descriptions, cancellationToken);

    /// <summary>按产品编辑权限从所选产品的主型号拉取中英文配置。</summary>
    [HttpPost("pull-master-features")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterFeaturesAsync([FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken)
        => masterMaintenance.PullProductsAsync(request, ProductMasterSyncFields.Features, cancellationToken);

    /// <summary>按产品编辑权限从所选产品的主型号拉取默认供应商。</summary>
    [HttpPost("pull-master-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterSupplierAsync([FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken)
        => masterMaintenance.PullProductsAsync(request, ProductMasterSyncFields.Supplier, cancellationToken);

    /// <summary>按产品编辑权限直接拉取主型号每箱毛重和体积到产品单位字段。</summary>
    [HttpPost("pull-master-weight-volume")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterWeightVolumeAsync([FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken)
        => masterMaintenance.PullProductsAsync(request, ProductMasterSyncFields.WeightVolume, cancellationToken);

    /// <summary>按产品编辑权限一次拉取四组主型号资料。</summary>
    [HttpPost("pull-master-all")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterAllAsync([FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken)
        => masterMaintenance.PullProductsAsync(request, ProductMasterSyncFields.All, cancellationToken);

    /// <summary>产品维护专用的活动仓库参考候选；不开放库存模块的仓库查询。</summary>
    [Authorize]
    [HttpGet("default-warehouse-candidates")]
    public Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidates(
        [FromQuery] PagedRequestBase request, CancellationToken cancellationToken) =>
        query.GetDefaultWarehouseCandidatesAsync(request, cancellationToken);
}
