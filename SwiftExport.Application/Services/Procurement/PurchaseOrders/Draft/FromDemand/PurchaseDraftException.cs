namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand;

/// <summary>保留需求转采购原有稳定业务错误码，供预览与来源创建共同转换。</summary>
internal sealed class PurchaseDraftException(string code, string message) : Exception(message)
{
    /// <summary>客户端可识别的采购草稿错误码。</summary>
    public string Code { get; } = code;
}
