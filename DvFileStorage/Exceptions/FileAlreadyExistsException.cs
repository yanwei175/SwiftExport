namespace DvFileStorage.Exceptions;

/// <summary>表示默认拒绝覆盖时，目标逻辑文件已经存在。</summary>
public sealed class FileAlreadyExistsException : IOException
{
    /// <summary>使用不包含物理根目录的逻辑路径创建冲突异常。</summary>
    public FileAlreadyExistsException(string logicalPath, Exception? innerException = null)
        : base($"目标文件已存在：{logicalPath}", innerException)
    {
        LogicalPath = logicalPath;
    }

    /// <summary>获取发生冲突的逻辑相对路径。</summary>
    public string LogicalPath { get; }
}
