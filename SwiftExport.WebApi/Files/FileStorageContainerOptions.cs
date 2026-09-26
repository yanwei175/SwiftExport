namespace SwiftExport.WebApi.Files;

/// <summary>承载宿主 JSON 中单个显式容器的物理根目录和上传上限。</summary>
public sealed class FileStorageContainerOptions
{
    /// <summary>获取或设置必须已存在的绝对物理根目录。</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>获取或设置该容器单次上传允许的最大字节数。</summary>
    public long MaxUploadBytes { get; set; }
}
