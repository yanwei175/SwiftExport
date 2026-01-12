using SwiftExport.Core.Common;


namespace SwiftExport.Core.Entities.Orders;

public class ReceiptItem : BaseEntity
{
    public string? PoNo { get; set; }
    public long? PoLineID { get; set; }
    public string? SKU { get; set; }
    public string? SupplierCode { get; set; }
    public decimal? ReceiveQTY { get; set; }
    public string? Note { get; set; }

}

