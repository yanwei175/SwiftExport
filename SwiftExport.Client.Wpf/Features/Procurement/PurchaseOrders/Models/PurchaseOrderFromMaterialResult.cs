namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>统一采购弹窗关闭后返回给采购管理页的可判别结果。</summary>
public sealed class PurchaseOrderFromMaterialResult
{
    /// <summary>本次流程是否产生成功或已应用结果。</summary>
    public bool HasAppliedResults { get; init; }
}
