using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购入库头实体（对应 dbo.PurchaseReceiveHead）。
/// </summary>
[Table("PurchaseReceiveHead")]
public class ReceiveHead : BaseEntity
{
    /// <summary>
    /// 入库单号（业务主键）。
    /// </summary>
    public string ReceiveNo { get; set; } = string.Empty;

    /// <summary>
    /// 供应商编码。
    /// </summary>
    public string? SupplierCode { get; set; }

    /// <summary>
    /// 入库日期。
    /// </summary>
    public DateTime? ReceiveDate { get; set; }

    /// <summary>
    /// 操作人。
    /// </summary>
    public string? Handler { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; set; }

    // 增加一个冗余字段，方便在列表页直接搜索 PO 号，而不需要 Join
    // 存储示例："PO24001, PO24002"
    public string? RelatedPoNos { get; set; }
}
