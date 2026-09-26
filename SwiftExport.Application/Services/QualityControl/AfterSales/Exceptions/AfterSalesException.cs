namespace SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
/// <summary>售后可预期失败，使用稳定代码及字段路径反馈。</summary>
public sealed class AfterSalesException(string code, string message, string? field = null) : Exception(message)
{
    /// <summary>业务错误代码。</summary>
    public string Code { get; } = code;
    /// <summary>字段路径，非字段失败为空。</summary>
    public string? Field { get; } = field;
}
