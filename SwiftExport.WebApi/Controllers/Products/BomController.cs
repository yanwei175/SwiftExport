using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Products;

/// <summary>承载共享 BOM 组成操作及管理页只读查询的 HTTP 边界，不直接处理数据库事实。</summary>
public class BomController(IBomService service) : BaseController<Bom, BomDto>(service)
{
    /// <summary>读取独立 BOM 管理成品列表，不复用普通产品整包投影。</summary>
    [HttpPost("products/page")]
    public Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>> SearchProducts(
        [FromBody] BomProductSearchRequest request,
        [FromServices] SwiftExport.Application.Interfaces.Products.BomManagement.IBomManagementQueryService query,
        CancellationToken cancellationToken) => query.SearchProductsAsync(request, cancellationToken);

    /// <summary>预览编号占用和共享数量，正式绑定仍独立复核。</summary>
    [HttpGet("number-info")]
    public Task<ServiceResult<BomNumberInfoDto>> GetNumberInfo([FromQuery] string bomNo,
        [FromServices] SwiftExport.Application.Interfaces.Products.BomManagement.IBomManagementQueryService query,
        CancellationToken cancellationToken) => query.GetNumberInfoAsync(bomNo, cancellationToken);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<BomBatchSaveResponse>> BatchSave([FromBody] BomBatchSaveRequest req) => await service.BatchSaveAsync(req);

    /// <summary>可取消地读取已保存多层结构。</summary>
    [HttpGet("tree/{productSku}")]
    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTree(string productSku, CancellationToken cancellationToken)
    {
        return await service.GetBomTreeAsync(productSku, cancellationToken);
    }

    /// <summary>可取消地读取当前共享组成，空 BOM 仍为成功读取。</summary>
    [HttpGet("by-no/{bomNo}")]
    public async Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNo(string bomNo, CancellationToken cancellationToken)
    {
        return await service.GetBomDtoByBomNoAsync(bomNo, cancellationToken);
    }


    /// <summary>对已保存结构执行现有校验。</summary>
    [HttpGet("validate/{productSku}")]
    public async Task<ServiceResult<bool>> ValidateBom(string productSku, CancellationToken cancellationToken)
    {
        return await service.ValidateBomAsync(productSku, cancellationToken);
    }
}
