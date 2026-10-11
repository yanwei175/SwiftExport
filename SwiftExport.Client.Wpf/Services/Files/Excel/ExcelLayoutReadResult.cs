using System.Collections.ObjectModel;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>脱离工作簿生命周期的固定字段快照，保留空明细行和调用方字段键。</summary>
public sealed class ExcelLayoutReadResult
{
    /// <summary>复制单头、明细和问题集合，不让调用方集合或后续文件操作改变当前结果。</summary>
    public ExcelLayoutReadResult(
        string worksheetName,
        IReadOnlyDictionary<string, FileCell> header,
        IEnumerable<IReadOnlyDictionary<string, FileCell>> rows,
        IEnumerable<FileIssue>? issues = null)
    {
        WorksheetName = worksheetName;
        Header = CopyDictionary(header);
        Rows = Array.AsReadOnly(rows.Select(CopyDictionary).ToArray());
        Issues = Array.AsReadOnly((issues ?? []).ToArray());
    }

    /// <summary>本次明确选择的工作表名称。</summary>
    public string WorksheetName { get; }

    /// <summary>读取一次的固定字段，键不作反射赋值解释。</summary>
    public IReadOnlyDictionary<string, FileCell> Header { get; }

    /// <summary>按原始明细行顺序保存的字段字典，中间空行不删除。</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, FileCell>> Rows { get; }

    /// <summary>可定位的单元格问题，不包含业务内容值。</summary>
    public IReadOnlyList<FileIssue> Issues { get; }

    // 字段键按序号比较，避免区域性或大小写折叠把调用方不同字段合并。
    private static IReadOnlyDictionary<string, FileCell> CopyDictionary(IReadOnlyDictionary<string, FileCell> source) =>
        new ReadOnlyDictionary<string, FileCell>(source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}
