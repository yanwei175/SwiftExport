using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Files;

/// <summary>隔离文件读取与写入权限决策，使文件用例不依赖 HTTP 或角色声明细节。</summary>
public interface IFileAccessPolicy
{
    /// <summary>验证当前调用方是否允许读取受保护容器。</summary>
    Task<ServiceResult<bool>> AuthorizeReadAsync(CancellationToken cancellationToken = default);

    /// <summary>验证当前调用方是否允许上传或覆盖受保护容器。</summary>
    Task<ServiceResult<bool>> AuthorizeWriteAsync(CancellationToken cancellationToken = default);
}
