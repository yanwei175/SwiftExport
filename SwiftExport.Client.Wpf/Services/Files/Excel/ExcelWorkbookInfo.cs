namespace SwiftExportClient.Services.Files.Excel;

/// <summary>不携带第三方工作簿对象的工作表名称快照，供调用方明确选择本次读取目标。</summary>
public sealed class ExcelWorkbookInfo
{
    /// <summary>复制工作表名称，避免工作簿释放或外部集合修改影响快照。</summary>
    public ExcelWorkbookInfo(IEnumerable<string> worksheetNames) =>
        WorksheetNames = Array.AsReadOnly(worksheetNames.ToArray());

    /// <summary>按工作簿顺序保存的工作表名称。</summary>
    public IReadOnlyList<string> WorksheetNames { get; }
}
