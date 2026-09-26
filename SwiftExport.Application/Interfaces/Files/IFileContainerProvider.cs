using SwiftExport.Application.Interfaces.Files.Models;

namespace SwiftExport.Application.Interfaces.Files;

/// <summary>按稳定容器标识解析启动配置快照，不暴露物理根目录。</summary>
public interface IFileContainerProvider
{
    /// <summary>尝试解析可访问的显式容器；未知标识返回 false。</summary>
    bool TryGetContainer(string containerId, out FileContainer? container);
}
