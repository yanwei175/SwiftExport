namespace SwiftExportClient.Services.Files;

/// <summary>区分可供业务进一步处理的警告与阻止文件操作的错误。</summary>
public enum FileIssueSeverity
{
    /// <summary>快照已读取，调用方应按业务规则判断能否使用。</summary>
    Warning,
    /// <summary>数据、布局、模板或文件操作错误。</summary>
    Error,
}
