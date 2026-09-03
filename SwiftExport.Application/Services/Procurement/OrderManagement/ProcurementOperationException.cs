using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>携带新采购稳定错误代码和字段路径的预期业务拒绝，不用于技术异常或取消。</summary>
public sealed class ProcurementOperationException(string code, string message, string? field = null) : Exception(message)
{
    /// <summary>稳定错误代码。</summary>
    public string Code { get; } = code;

    /// <summary>可选命令字段路径。</summary>
    public string? Field { get; } = field;

    /// <summary>建立字段校验失败。</summary>
    public static ProcurementOperationException Validation(string message, string? field = null)
        => new(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Validation, message, field);

    /// <summary>建立一般业务状态阻断。</summary>
    public static ProcurementOperationException Blocked(string message)
        => new(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, message);
}

