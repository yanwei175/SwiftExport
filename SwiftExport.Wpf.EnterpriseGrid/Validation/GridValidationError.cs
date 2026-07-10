namespace SwiftExport.Wpf.EnterpriseGrid.Validation;

/// <summary>
/// 可绑定、可虚拟化的 Grid 验证错误引用，不持有 DataGridRow 或 DataGridCell 容器。
/// </summary>
/// <param name="Item">错误所属数据项；Grid 级错误可为空。</param>
/// <param name="ColumnKey">错误所属稳定列标识；行级错误可为空。</param>
/// <param name="Message">面向用户的文本错误信息。</param>
public sealed record GridValidationError(object? Item, string? ColumnKey, string Message);
