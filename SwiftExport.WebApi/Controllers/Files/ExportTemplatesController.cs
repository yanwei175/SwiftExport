using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Files;

/// <summary>提供受保护的服务器模板列表和固定版本 XLSX 下载，不暴露路径、上传或目录浏览。</summary>
[ApiController]
[Authorize]
[Route("api/export-templates")]
public sealed class ExportTemplatesController(IExportTemplateCatalogService catalog) : ControllerBase
{
    /// <summary>按精确单据类型与数据契约列举当前启用模板；取消令牌透传到文件校验。</summary>
    [HttpGet]
    public Task<ServiceResult<List<ExportTemplateInfoDto>>> ListAsync(
        [FromQuery] string documentType,
        [FromQuery] string dataContractKey,
        [FromQuery] int dataContractVersion,
        CancellationToken cancellationToken = default) =>
        catalog.ListAsync(documentType, dataContractKey, dataContractVersion, cancellationToken);

    /// <summary>返回已验证的同一内存快照，由框架释放流；响应须在复用前验证，不公开服务器物理目录。</summary>
    [HttpGet("{templateId}/versions/{templateVersion}/content")]
    public async Task<IActionResult> DownloadAsync(
        string templateId,
        int templateVersion,
        CancellationToken cancellationToken = default)
    {
        var result = await catalog.OpenReadAsync(templateId, templateVersion, cancellationToken);
        if (!result.Success) return new ObjectResult(result);

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.Data.FileName);
    }
}
