namespace SwiftExport.Application.Commands;

/// <summary>公共命令执行失败，保留可向客户端返回的稳定错误码和字段。</summary>
public sealed class BusinessCommandException(string code, string message, string? field = null) : Exception(message)
{
    /// <summary>已有 HTTP 契约使用的错误码。</summary>
    public string Code { get; } = code;
    /// <summary>可选的请求字段路径。</summary>
    public string? Field { get; } = field;
}
