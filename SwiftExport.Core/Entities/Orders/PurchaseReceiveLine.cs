using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购入库行实体（对应 dbo.PurchaseReceiveLine）。
/// </summary>
[Table("PurchaseReceiveLine")]
public class PurchaseReceiveLine : BaseEntity
{
    /// <summary>
    /// 关联头表单号。
    /// </summary>
    public string ReceiveNo { get; set; } = string.Empty;

    /// <summary>
    /// 物料 SKU。
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// 本次入库数量。
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// 单位。
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 关联采购单行ID（用于回写采购行实收数量）。
    /// </summary>
    public long? PoLineId { get; set; }

    /// <summary>
    /// 关联销售单行ID（用于回写需求池已收货数量）。
    /// </summary>
    public long? SoLineId { get; set; }
}
