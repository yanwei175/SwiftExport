using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.WebApi.Controllers.Files;

/// <summary>提供实时 Admin 保护的产品图片 multipart 批量上传 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/product-images")]
public sealed class ProductImageController(IProductImageUploadService uploads) : ControllerBase
{
    /// <summary>顺序解析同名 files 文件节；批次正常完成时返回允许部分成功的有序结果。</summary>
    [HttpPost("batch")]
    [DisableFormValueModelBinding]
    public async Task<ActionResult<ServiceResult<ProductImageBatchUploadResultDto>>> UploadBatch(
        CancellationToken cancellationToken)
    {
        // 实时 Admin 校验必须先于读取主体、设置 multipart reader 或触发目录创建。
        var started = await uploads.BeginBatchAsync(cancellationToken).ConfigureAwait(false);
        if (!started.Success) return CopyFailure(started);
        var session = started.Data;

        if (Request.ContentLength > session.MaxBatchBytes)
            return TooLarge("上传批次超过请求总量上限。");
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<ProductImageBatchUploadResultDto>.Fail(
                FileErrorCodes.UnsupportedMediaType,
                "上传请求必须使用 multipart/form-data。",
                "Content-Type");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 128)
        {
            return ServiceResult<ProductImageBatchUploadResultDto>.Fail(
                ServiceErrorCodes.Validation,
                "multipart boundary 缺失或过长。",
                "Content-Type");
        }

        var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = session.MaxBatchBytes;

        var fileCount = 0;
        try
        {
            // 外层服务器限制和本计数流共同覆盖无长度或虚报长度；包装器不释放 Request.Body。
            using var limitedBody = new BatchRequestLimitStream(Request.Body, session.MaxBatchBytes);
            var reader = new MultipartReader(boundary, limitedBody)
            {
                HeadersCountLimit = 16,
                HeadersLengthLimit = 16 * 1024
            };
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var disposition = section.GetContentDispositionHeader();
                if (disposition is null || !disposition.IsFileDisposition() ||
                    !string.Equals(disposition.Name.Value?.Trim('"'), "files", StringComparison.Ordinal))
                {
                    return ServiceResult<ProductImageBatchUploadResultDto>.Fail(
                        ServiceErrorCodes.Validation,
                        "multipart 只允许名为 files 的文件字段。",
                        "files");
                }

                fileCount++;
                if (fileCount > session.MaxFiles)
                    return TooLarge("上传文件数量超过批次上限。");
                var fileName = HeaderUtilities.RemoveQuotes(
                    disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                await uploads.UploadItemAsync(
                    session,
                    fileCount - 1,
                    fileName ?? string.Empty,
                    section.Body,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ProductImageBatchLimitExceededException)
        {
            return TooLarge("上传批次超过请求总量上限。");
        }
        catch (InvalidDataException)
        {
            return ServiceResult<ProductImageBatchUploadResultDto>.Fail(
                ServiceErrorCodes.Validation,
                "multipart 请求格式无效。",
                "body");
        }

        if (fileCount == 0)
        {
            return ServiceResult<ProductImageBatchUploadResultDto>.Fail(
                ServiceErrorCodes.Validation,
                "批次至少需要一个 files 文件字段。",
                "files");
        }

        return ServiceResult<ProductImageBatchUploadResultDto>.Ok(uploads.CompleteBatch(session));
    }

    private static ServiceResult<ProductImageBatchUploadResultDto> CopyFailure(
        ServiceResult<ProductImageBatchSession> source) =>
        ServiceResult<ProductImageBatchUploadResultDto>.Fail(
            source.Error?.Code ?? ServiceErrorCodes.Unexpected,
            source.Error?.Message ?? source.Message,
            source.Error?.Field);

    private static ServiceResult<ProductImageBatchUploadResultDto> TooLarge(string message) =>
        ServiceResult<ProductImageBatchUploadResultDto>.Fail(
            FileErrorCodes.UploadTooLarge,
            message,
            "body");
}
