using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 库存实体（对应 dbo.Inventory）。
/// </summary>
[Table("Inventory")]
public class Inventory : BaseEntity
{
    /// <summary>
    /// 仓库代码。
    /// </summary>
    public string WhseCode { get; set; } = string.Empty;

    /// <summary>
    /// 库位代码（当前用于存供应商编码）。
    /// </summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>
    /// 物料 SKU。
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// 单位。
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 现存量。
    /// </summary>
    public decimal OnhandQty { get; set; }
}
