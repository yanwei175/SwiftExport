using System.IO;
using Microsoft.Extensions.Logging;
using Refit;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Products.ProductList.Services;

/// <summary>在后台打开文件，通过现有认证 API 管线流式上传并释放流。</summary>
public sealed class ProductImageUploadClient(
    IProductImageApi api, IApiCallRunner runner, ILogger<ProductImageUploadClient> logger) : IProductImageUploadClient
{
    /// <inheritdoc />
    public Task<ServiceResult<ProductImageBatchUploadResultDto>> UploadAsync(string path) => Task.Run(async () =>
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await runner.RunAsync(
                () => api.UploadAsync([new StreamPart(stream, Path.GetFileName(path), "application/octet-stream")]),
                "图片上传失败");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("产品图片读取或传输失败，异常类型 {ExceptionType}", exception.GetType().Name);
            return ServiceResult<ProductImageBatchUploadResultDto>.Fail(ServiceErrorCodes.OutcomeUnknown,
                "文件无法读取或传输中断；请检查文件和服务器图片后再重传。");
        }
    });
}
