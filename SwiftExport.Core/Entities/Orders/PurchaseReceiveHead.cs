using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购入库头实体（对应 dbo.PurchaseReceiveHead）。
/// </summary>
[Table("PurchaseReceiveHead")]
public class PurchaseReceiveHead : BaseEntity
{
    /// <summary>
    /// 入库单号（业务主键）。
    /// </summary>
    public string ReceiveNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联采购业务单号。
    /// </summary>
    public string? PoNo { get; set; }

    /// <summary>
    /// 关联采购系统内码。
    /// </summary>
    public string? OrderNo { get; set; }

    /// <summary>
    /// 供应商编码。
    /// </summary>
    public string? SupplierCode { get; set; }

    /// <summary>
    /// 入库/装柜日期。
    /// </summary>
    public DateTime? ReceiveDate { get; set; }

    /// <summary>
    /// 操作人。
    /// </summary>
    public string? Handler { get; set; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; set; }
}
