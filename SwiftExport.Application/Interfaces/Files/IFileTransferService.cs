using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Files;

/// <summary>编排受保护容器的流式上传与读取，不接收 ASP.NET 请求对象。</summary>
public interface IFileTransferService
{
    /// <summary>校验权限、容器与大小后流式发布文件，源流仍由调用方释放。</summary>
    Task<ServiceResult<FileUploadResultDto>> UploadAsync(
        string containerId,
        string path,
        Stream source,
        long? contentLength,
        bool overwrite,
        CancellationToken cancellationToken = default);

    /// <summary>校验权限和容器后打开读取流；成功结果中的流转交 HTTP 框架释放。</summary>
    Task<ServiceResult<FileReadHandle>> OpenReadAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default);
}
