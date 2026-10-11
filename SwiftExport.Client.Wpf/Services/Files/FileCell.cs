namespace SwiftExportClient.Services.Files;

/// <summary>保留单元格的原始值、显示文本和来源位置；不依赖工作簿或 WPF 对象。</summary>
/// <param name="RowNumber">文件中的一基行号；CSV 的行号约定见读取选项。</param>
/// <param name="ColumnNumber">文件中的一基列号。</param>
/// <param name="Kind">原始值类型。</param>
/// <param name="RawValue">空值、string、double、bool、DateTime 或 TimeSpan；不进行业务转换。</param>
/// <param name="DisplayText">按读取文化及单元格格式生成的显示文本。</param>
public sealed record FileCell(int RowNumber, int ColumnNumber, FileCellKind Kind, object? RawValue, string DisplayText)
{
    /// <summary>Excel 中请求读取的位置，采用 A1 地址。</summary>
    public string? Address { get; init; }

    /// <summary>已有公式文本；读取公式结果只使用文件中的缓存。</summary>
    public string? Formula { get; init; }

    /// <summary>自定义数字格式代码；内置格式另由 NumberFormatId 表达。</summary>
    public string? NumberFormat { get; init; }

    /// <summary>Excel 数字格式编号。</summary>
    public int? NumberFormatId { get; init; }

    /// <summary>所属合并区域，未合并时为空。</summary>
    public string? MergedRange { get; init; }

    /// <summary>实际取值来源；固定地址落在合并区域内时可指向左上角。</summary>
    public string? SourceAddress { get; init; }
}
