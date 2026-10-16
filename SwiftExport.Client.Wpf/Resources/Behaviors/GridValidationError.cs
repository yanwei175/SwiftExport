using System.Windows.Controls;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>
/// 模型软验证错误引用，不持有 DataGridRow 或 DataGridCell 容器。
/// </summary>
/// <param name="Item">错误所属模型数据项。</param>
/// <param name="Column">错误所属原生列对象；行级错误可为空。</param>
/// <param name="Message">面向用户的文本错误信息。</param>
public sealed record GridValidationError(object? Item, DataGridColumn? Column, string Message);
