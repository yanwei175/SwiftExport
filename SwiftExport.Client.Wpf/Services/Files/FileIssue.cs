namespace SwiftExportClient.Services.Files;

/// <summary>提供稳定诊断代码及可选文件位置，供业务页面定位和展示，不携带单元格敏感内容。</summary>
public sealed record FileIssue(
    string Code,
    string Message,
    string? WorksheetName = null,
    int? RowNumber = null,
    int? ColumnNumber = null,
    string? Address = null,
    string? FieldKey = null,
    FileIssueSeverity Severity = FileIssueSeverity.Warning);
