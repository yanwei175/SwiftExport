using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Files;

/// <summary>提供显式容器的受保护原始流上传和 attachment 文件读取边界。</summary>
[ApiController]
[Authorize]
[Route("api/files")]
public sealed class FileController(
    IFileTransferService transfers,
    IFileContainerProvider containers) : ControllerBase
{
    /// <summary>从原始 application/octet-stream 请求体上传文件，默认拒绝覆盖。</summary>
    [HttpPut("{containerId}")]
    public async Task<ActionResult<ServiceResult<FileUploadResultDto>>> Upload(
        string containerId,
        [FromQuery] string path,
        [FromQuery] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<FileUploadResultDto>.Fail(
                FileErrorCodes.UnsupportedMediaType,
                "上传请求必须使用 application/octet-stream。",
                "Content-Type");
        }

        // 在 MVC 未读取原始主体时设置端点级外层限制；应用层计数流仍负责无长度或虚报长度。
        if (containers.TryGetContainer(containerId, out var container) && container is not null)
        {
            var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = GetOuterRequestLimit(container.MaxUploadBytes);
        }

        return await transfers.UploadAsync(
            containerId,
            path,
            Request.Body,
            Request.ContentLength,
            overwrite,
            cancellationToken);
    }

    /// <summary>返回统一 attachment 下载；GET 支持 Range，HEAD 由文件结果只写响应头。</summary>
    [HttpGet("{containerId}")]
    [HttpHead("{containerId}")]
    public async Task<IActionResult> Download(
        string containerId,
        [FromQuery] string path,
        CancellationToken cancellationToken = default)
    {
        var result = await transfers.OpenReadAsync(containerId, path, cancellationToken);
        if (!result.Success) return new ObjectResult(result);

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(
            result.Data.Content,
            "application/octet-stream",
            result.Data.FileName,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// 为 chunked 编码保留有限协议开销；文件净字节上限仍由 Application 计数流严格执行。
    /// </summary>
    private static long GetOuterRequestLimit(long maxUploadBytes)
    {
        const long protocolAllowance = 64 * 1024;
        return maxUploadBytes > long.MaxValue - protocolAllowance
            ? long.MaxValue
            : maxUploadBytes + protocolAllowance;
    }
}
