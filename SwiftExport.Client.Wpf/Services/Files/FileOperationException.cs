namespace SwiftExportClient.Services.Files;

/// <summary>表达文件读取或模板导出的整体失败；技术原因保留在内部异常中，取消不使用此异常。</summary>
public sealed class FileOperationException : Exception
{
    /// <summary>建立可分类的失败及定位诊断；消息应适合由调用方转换成用户反馈。</summary>
    public FileOperationException(
        string code,
        string message,
        Exception? innerException = null,
        IReadOnlyList<FileIssue>? issues = null)
        : base(message, innerException)
    {
        Code = code;
        Issues = Array.AsReadOnly(issues?.ToArray() ??
            [new FileIssue(code, message, Severity: FileIssueSeverity.Error)]);
    }

    /// <summary>稳定的错误分类代码，不依赖异常消息匹配。</summary>
    public string Code { get; }

    /// <summary>本次操作的只读诊断快照。</summary>
    public IReadOnlyList<FileIssue> Issues { get; }
}
