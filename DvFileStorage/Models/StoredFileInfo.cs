namespace DvFileStorage.Models;

/// <summary>描述存储中的文件元数据，不暴露宿主物理根目录。</summary>
public sealed class StoredFileInfo
{
    /// <summary>获取使用正斜线分隔的逻辑相对路径。</summary>
    public required string Path { get; init; }

    /// <summary>获取文件字节长度。</summary>
    public required long Length { get; init; }

    /// <summary>获取文件最后修改的 UTC 时间。</summary>
    public required DateTimeOffset LastModifiedUtc { get; init; }
}
