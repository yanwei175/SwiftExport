using DvFileStorage.Abstractions;

namespace SwiftExport.Application.Interfaces.Files.Models;

/// <summary>表示一个已验证的容器配置快照及其独立底层存储。</summary>
public sealed class FileContainer
{
    /// <summary>获取稳定的小写容器标识。</summary>
    public required string Id { get; init; }

    /// <summary>获取该容器独立的文件存储实例。</summary>
    public required IFileStorage Storage { get; init; }

    /// <summary>获取单次上传允许的最大字节数。</summary>
    public required long MaxUploadBytes { get; init; }

    /// <summary>获取用于生成受保护读取地址的客户端可达 API 基址。</summary>
    public required Uri ApiBaseUri { get; init; }
}
