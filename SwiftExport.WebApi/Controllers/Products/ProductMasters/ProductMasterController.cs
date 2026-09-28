using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.ProductMasters;

/// <summary>主产品 HTTP 边界，仅负责路由、绑定和委托应用服务。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductMasterController(IProductMasterQueryService query, IProductMasterMaintenanceService maintenance) : ControllerBase
{
    /// <summary>分页查询主型号。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(
        [FromBody] ProductMasterSearchRequest request,
        CancellationToken cancellationToken) => query.GetPagedAsync(request, cancellationToken);

    /// <summary>批量设置主型号分类并防止短时间重复提交。</summary>
    [HttpPost("assign-category")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
        [FromBody] AssignProductMastersToCategoryRequest request,
        CancellationToken cancellationToken) => maintenance.AssignCategoryAsync(request, cancellationToken);

    /// <summary>读取主型号及全部有效中性产品。</summary>
    [HttpGet("{productMasterId:guid}")]
    public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(
        Guid productMasterId,
        CancellationToken cancellationToken) => query.GetDetailAsync(productMasterId, cancellationToken);

    /// <summary>接收批量主型号变更及同步选项，防短时间重复提交后交由应用服务处理。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(
        [FromBody] ProductMasterBatchSaveRequest request,
        CancellationToken cancellationToken) => maintenance.BatchSaveAsync(request, cancellationToken);

    /// <summary>原子保存主型号详情及中性产品变更并防止短时间重复提交。</summary>
    [HttpPost("save-detail")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(
        [FromBody] SaveProductMasterDetailRequest request,
        CancellationToken cancellationToken) => maintenance.SaveDetailAsync(request, cancellationToken);

    /// <summary>按产品编辑权限下发中英文描述。</summary>
    [HttpPost("sync-descriptions")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncDescriptionsAsync([FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken)
        => maintenance.SyncProductsAsync(request, ProductMasterSyncFields.Descriptions, cancellationToken);

    /// <summary>按产品编辑权限下发中英文配置。</summary>
    [HttpPost("sync-features")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncFeaturesAsync([FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken)
        => maintenance.SyncProductsAsync(request, ProductMasterSyncFields.Features, cancellationToken);

    /// <summary>按产品编辑权限下发默认供应商。</summary>
    [HttpPost("sync-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncSupplierAsync([FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken)
        => maintenance.SyncProductsAsync(request, ProductMasterSyncFields.Supplier, cancellationToken);

    /// <summary>按产品编辑权限下发毛重和体积。</summary>
    [HttpPost("sync-weight-volume")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncWeightVolumeAsync([FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken)
        => maintenance.SyncProductsAsync(request, ProductMasterSyncFields.WeightVolume, cancellationToken);

    /// <summary>按产品编辑权限同时下发四组资料。</summary>
    [HttpPost("sync-all")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SyncProductMasterDataResponse>> SyncAllAsync([FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken)
        => maintenance.SyncProductsAsync(request, ProductMasterSyncFields.All, cancellationToken);
}
