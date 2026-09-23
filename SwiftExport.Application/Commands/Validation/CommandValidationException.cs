namespace SwiftExport.Application.Commands.Validation;

/// <summary>表示命令请求结构无效，只携带可安全返回的中文消息和字段路径。</summary>
public sealed class CommandValidationException(string message, string? field) : Exception(message)
{
    /// <summary>无效字段在请求 DTO 中的路径。</summary>
    public string? Field { get; } = field;
}
