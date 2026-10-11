namespace SwiftExportClient.Services.Files;

/// <summary>表达按列序号排列的表头、明细和诊断；不把重复或空表头合并成字典。</summary>
public sealed class TableReadResult
{
    /// <summary>复制读取结果形成独立只读快照，返回后不再依赖解析器或输入流。</summary>
    public TableReadResult(
        string? worksheetName,
        IEnumerable<FileCell> headers,
        IEnumerable<IReadOnlyList<FileCell>> rows,
        IEnumerable<FileIssue>? issues = null)
    {
        WorksheetName = worksheetName;
        Headers = Array.AsReadOnly(headers.ToArray());
        Rows = Array.AsReadOnly(rows
            .Select(row => (IReadOnlyList<FileCell>)Array.AsReadOnly(row.ToArray())).ToArray());
        Issues = Array.AsReadOnly(issues?.ToArray() ?? []);
    }

    /// <summary>Excel 工作表名称；CSV 为空。</summary>
    public string? WorksheetName { get; }

    /// <summary>原始表头单元格，保留顺序、空值和重复名称。</summary>
    public IReadOnlyList<FileCell> Headers { get; }

    /// <summary>独立明细快照；每个单元格保留文件中的来源行列。</summary>
    public IReadOnlyList<IReadOnlyList<FileCell>> Rows { get; }

    /// <summary>单元格或行问题；调用方负责按业务规则决定接纳与否。</summary>
    public IReadOnlyList<FileIssue> Issues { get; }
}
