using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Products.ProductMasters;
/// <summary>仅转发父级预览、导入及导出，工作簿不进入服务器。</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductMasterImportExportController(IProductMasterImportService imports, IProductMasterQueryService query) : ControllerBase
{
    /// <summary>只读匹配及逐行校验。</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<ServiceResult<ProductMasterImportPreviewResult>> PreviewAsync([FromBody] ProductMasterImportPreviewRequest request, CancellationToken cancellationToken) => imports.PreviewAsync(request, cancellationToken);
    /// <summary>活动管理员全批提交，防重复过滤仅辅助防误点。</summary>
    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductMasterImportResult>> ImportAsync([FromBody] ProductMasterImportRequest request, CancellationToken cancellationToken) => imports.ImportAsync(request, cancellationToken);
    /// <summary>按已应用筛选读取完整有界结果。</summary>
    [HttpPost("export")]
    public Task<ServiceResult<List<ProductMasterExportRowDto>>> ExportAsync([FromBody] ProductMasterExportRequest request, CancellationToken cancellationToken) => query.ExportAsync(request, cancellationToken);
}
