using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>可安全显示给用户的入库草稿业务错误。</summary>
internal sealed class DraftException(string message, string code = ServiceErrorCodes.BusinessBlocked, string? field = null) : Exception(message)
{
    /// <summary>向客户端返回的稳定业务错误码。</summary>
    public string Code { get; } = code;
    /// <summary>可选的输入字段名。</summary>
    public string? Field { get; } = field;
}
