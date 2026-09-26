namespace SwiftExport.Application.Services.Shipping;

/// <summary>表示可安全返回给客户端的装箱单业务失败。</summary>
public sealed class PackingListException(string code, string message, string? field = null) : Exception(message)
{
    /// <summary>稳定业务错误代码。</summary>
    public string Code { get; } = code;
    /// <summary>发生错误的契约字段路径；无法定位时为空。</summary>
    public string? Field { get; } = field;
}
