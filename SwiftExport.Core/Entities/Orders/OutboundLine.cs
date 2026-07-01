using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 出库单行实体，出库库存流水应关联到具体出库行。
/// </summary>
[Table("OutboundLine")]
public class OutboundLine : BaseEntity
{
    public int LineNumber { get; set; } = 1;

    /// <summary>
    /// 出库单头 ID。
    /// </summary>
    public required Guid OutboundId { get; set; }

    /// <summary>
    /// 出库单号。
    /// </summary>
    public required string OutboundNo { get; set; }

    /// <summary>
    /// 物料 SKU。
    /// </summary>
    public required string Sku { get; set; }

    /// <summary>
    /// 出库数量。
    /// </summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// 单位。
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 出库仓库编码。
    /// </summary>
    public string? WarehouseCode { get; set; }

    /// <summary>
    /// 出库库位编码。
    /// </summary>
    public string? LocationCode { get; set; }

    /// <summary>
    /// 来源类型。
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// 来源单号。
    /// </summary>
    public string? SourceNo { get; set; }

    /// <summary>
    /// 来源行 ID。
    /// </summary>
    public Guid? SourceLineId { get; set; }

    /// <summary>
    /// 参考单号，用于追溯采购单、箱单等上游单据。
    /// </summary>
    public string? RefNo { get; set; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; set; }
}
