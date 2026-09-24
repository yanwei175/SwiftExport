using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Application.Services.Inventory.Posting;

/// <summary>表示库存过账因业务规则或幂等冲突而被拒绝。</summary>
public sealed class InventoryPostingException(string message, string code = ServiceErrorCodes.BusinessBlocked) : Exception(message)
{
    /// <summary>稳定业务错误类别，由调用方透传，不从异常消息推断。</summary>
    public string Code { get; } = code;
}
