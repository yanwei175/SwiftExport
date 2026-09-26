namespace SwiftExport.Application.Services.Files;

/// <summary>表示无长度或虚报长度的上传流在读取过程中超过容器上限。</summary>
internal sealed class FileUploadLimitExceededException : IOException
{
    /// <summary>使用容器限制创建不包含文件内容的超限异常。</summary>
    public FileUploadLimitExceededException(long maxUploadBytes)
        : base($"上传内容超过容器允许的 {maxUploadBytes} 字节上限。")
    {
        MaxUploadBytes = maxUploadBytes;
    }

    /// <summary>获取容器允许的最大字节数。</summary>
    public long MaxUploadBytes { get; }
}
