using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// Packing List header. The document links purchase receipt, sales shipment, and BOM component write-off.
/// </summary>
[Table("PackingListHead")]
public class PackingListHead : BaseEntity
{
    public required string PackingListNo { get; set; }
    public required string CustomerCode { get; set; }

    /// <summary>
    /// 这个发票号是为了应付老板的特殊需求,千万不要用这个发票号来执行业务逻辑
    /// </summary>
    public string? InvoiceNo { get; set; }
    public string? ShipToName { get; set; }
    public string? ShipToAddress { get; set; }
    public string? ShipToPhone { get; set; }
    public string? Attention { get; set; }

    public string? ContainerNo { get; set; }
    public string? SealNo { get; set; }
    public DateTime? ETD { get; set; }           // 预计发货日
    public DateTime? ETA { get; set; }           // 预计抵达日
    public bool IsPosted { get; set; }           // 是否过账（触发入库/回写的开关）
    public DateTime? PostedDate { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? PackingDate { get; set; }
    public string? Remark { get; set; }
}
