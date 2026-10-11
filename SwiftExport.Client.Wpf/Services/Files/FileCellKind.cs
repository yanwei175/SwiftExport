namespace SwiftExportClient.Services.Files;

/// <summary>描述文件中单元格的原始值类型，不替调用方推断业务字段类型。</summary>
public enum FileCellKind
{
    /// <summary>没有值的 Excel 单元格。</summary>
    Blank,
    /// <summary>文本；CSV 字段始终属于此类型。</summary>
    Text,
    /// <summary>Excel 数值，原始值为 double。</summary>
    Number,
    /// <summary>布尔值。</summary>
    Boolean,
    /// <summary>Excel 日期时间，不隐式转换时区。</summary>
    DateTime,
    /// <summary>Excel 时间间隔。</summary>
    TimeSpan,
    /// <summary>Excel 错误值，原始值为错误标识文本。</summary>
    Error,
}
