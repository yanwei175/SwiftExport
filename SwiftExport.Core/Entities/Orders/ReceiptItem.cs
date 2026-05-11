using SwiftExport.Core.Common;


namespace SwiftExport.Core.Entities.Orders;

public class ReceiptItem : BaseEntity
{
    public string? PoNo { get; set; }
    public long? PoLineID { get; set; }
    public string? Sku { get; set; }
    public string? SupplierCode { get; set; }
    /// <summary>
    /// 入库数量
    /// </summary>
    public decimal? ReceiveQTY { get; set; }
    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }

}

