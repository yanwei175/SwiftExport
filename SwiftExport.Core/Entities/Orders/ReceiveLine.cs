using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购入库行实体（对应 dbo.PurchaseReceiveLine）。
/// </summary>
[Table("PurchaseReceiveLine")]
public class ReceiveLine : BaseEntity
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
    public int? PoLineId { get; set; }

    /// <summary>
    /// 冗余单号：方便不查询 Join 也能知道这行货是哪个采购单的。
    /// </summary>
    public string? PoNo { get; set; }      // 该行属于哪个采购单号
}
