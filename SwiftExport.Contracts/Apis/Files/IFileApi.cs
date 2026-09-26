using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Files
{
    /// <summary>定义受保护容器的原始流上传、下载及元信息头契约。</summary>
    public interface IFileApi
    {
        /// <summary>以未缓冲的原始请求体上传文件；源流由调用方释放。</summary>
        [Put("/api/files/{containerId}")]
        [Headers("Content-Type: application/octet-stream")]
        Task<ServiceResult<FileUploadResultDto>> UploadAsync(
            string containerId,
            [AliasAs("path")] string path,
            [AliasAs("overwrite")] bool overwrite,
            [Body] Stream content,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>下载文件；响应及其内容流由调用方释放，可选传入标准 Range 头。</summary>
        [Get("/api/files/{containerId}")]
        Task<HttpResponseMessage> DownloadAsync(
            string containerId,
            [AliasAs("path")] string path,
            [Header("Range")] string range = null,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取文件响应头而不接收正文；响应由调用方释放。</summary>
        [Head("/api/files/{containerId}")]
        Task<HttpResponseMessage> HeadAsync(
            string containerId,
            [AliasAs("path")] string path,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
