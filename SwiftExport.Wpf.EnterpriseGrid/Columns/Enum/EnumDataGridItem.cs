namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Enum;

/// <summary>枚举便利列的显示项。</summary>
/// <param name="Value">真实枚举值；空项时为 null。</param>
/// <param name="DisplayName">面向用户的显示文本。</param>
public sealed record EnumDataGridItem(object? Value, string DisplayName);
