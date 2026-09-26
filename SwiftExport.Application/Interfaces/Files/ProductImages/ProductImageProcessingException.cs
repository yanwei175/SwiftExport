namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>表示图片内容可预期地不符合处理规格，并携带可安全回传的稳定错误码。</summary>
public sealed class ProductImageProcessingException(string errorCode, string message) : Exception(message)
{
    /// <summary>获取逐项结果使用的稳定错误码。</summary>
    public string ErrorCode { get; } = errorCode;
}
