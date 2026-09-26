namespace SwiftExport.Application.Interfaces.Files.Models;

/// <summary>把已打开的同一文件流及安全下载名交给 HTTP 文件响应。</summary>
public sealed class FileReadHandle
{
    /// <summary>获取由 HTTP 框架负责释放的读取流。</summary>
    public required Stream Content { get; init; }

    /// <summary>获取 Content-Disposition 使用的末级文件名。</summary>
    public required string FileName { get; init; }
}
