using DvFileStorage.Exceptions;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Files;

/// <summary>协调权限、显式容器、流量上限和底层完整发布语义。</summary>
public sealed class FileTransferService(
    IFileContainerProvider containers,
    IFileAccessPolicy accessPolicy) : IFileTransferService
{
    /// <inheritdoc />
    public async Task<ServiceResult<FileUploadResultDto>> UploadAsync(
        string containerId,
        string path,
        Stream source,
        long? contentLength,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            return ServiceResult<FileUploadResultDto>.Fail(
                ServiceErrorCodes.Validation, "上传请求体必须是可读流。", "body");
        var access = await accessPolicy.AuthorizeWriteAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Success) return CopyFailure<FileUploadResultDto>(access);
        if (!containers.TryGetContainer(containerId, out var container) || container is null)
            return ServiceResult<FileUploadResultDto>.Fail(ServiceErrorCodes.NotFound, "文件容器不存在。");
        if (contentLength is < 0)
            return ServiceResult<FileUploadResultDto>.Fail(ServiceErrorCodes.Validation, "Content-Length 不能为负数。", "Content-Length");
        if (contentLength > container.MaxUploadBytes)
            return TooLarge<FileUploadResultDto>();

        try
        {
            using var limited = new UploadLimitStream(source, container.MaxUploadBytes);
            await container.Storage.WriteAsync(path, limited, overwrite, cancellationToken).ConfigureAwait(false);
            return ServiceResult<FileUploadResultDto>.Ok(new FileUploadResultDto
            {
                ContainerId = container.Id,
                Path = path,
                Reference = $"{container.Id}/{path}",
                ReadUrl = BuildReadUrl(container, path)
            });
        }
        catch (FileUploadLimitExceededException)
        {
            return TooLarge<FileUploadResultDto>();
        }
        catch (FileAlreadyExistsException)
        {
            return ServiceResult<FileUploadResultDto>.Fail(
                FileErrorCodes.Conflict, "目标文件已存在；如需替换请显式允许覆盖。", "overwrite");
        }
        catch (ArgumentException)
        {
            return ServiceResult<FileUploadResultDto>.Fail(
                ServiceErrorCodes.Validation, "文件逻辑路径无效。", "path");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FileReadHandle>> OpenReadAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var access = await accessPolicy.AuthorizeReadAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Success) return CopyFailure<FileReadHandle>(access);
        if (!containers.TryGetContainer(containerId, out var container) || container is null)
            return ServiceResult<FileReadHandle>.Fail(ServiceErrorCodes.NotFound, "文件容器不存在。");

        try
        {
            var stream = await container.Storage.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            return ServiceResult<FileReadHandle>.Ok(new FileReadHandle
            {
                Content = stream,
                FileName = Path.GetFileName(path)
            });
        }
        catch (FileNotFoundException)
        {
            return ServiceResult<FileReadHandle>.Fail(ServiceErrorCodes.NotFound, "文件不存在。");
        }
        catch (ArgumentException)
        {
            return ServiceResult<FileReadHandle>.Fail(
                ServiceErrorCodes.Validation, "文件逻辑路径无效。", "path");
        }
    }

    /// <summary>使用配置基址和逐值转义的 query path 生成不依赖 Request.Host 的读取地址。</summary>
    private static string BuildReadUrl(FileContainer container, string path)
    {
        var relative = $"api/files/{Uri.EscapeDataString(container.Id)}?path={Uri.EscapeDataString(path)}";
        return new Uri(container.ApiBaseUri, relative).AbsoluteUri;
    }

    /// <summary>跨泛型复制访问拒绝，不改变原稳定错误码。</summary>
    private static ServiceResult<T> CopyFailure<T>(ServiceResult<bool> source) =>
        ServiceResult<T>.Fail(
            source.Error?.Code ?? ServiceErrorCodes.Unexpected,
            source.Error?.Message ?? source.Message,
            source.Error?.Field);

    /// <summary>生成统一的上传超限失败。</summary>
    private static ServiceResult<T> TooLarge<T>() =>
        ServiceResult<T>.Fail(FileErrorCodes.UploadTooLarge, "上传内容超过容器允许的大小。", "Content-Length");
}
