using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Products.ProductMasters;

/// <summary>转发父级导入批次，允许客户端连续提交，鉴权与逐行事务由应用服务执行。</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductMasterImportController(IProductMasterImportService imports) : ControllerBase
{
    /// <summary>每批最多一百行；网络中断不能据此推断之前各行是否提交。</summary>
    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<ServiceResult<BatchSaveResponseBase>> ImportAsync([FromBody] List<ProductMasterImportRowDto> rows, CancellationToken cancellationToken) => imports.ImportAsync(rows, cancellationToken);
}
