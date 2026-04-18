
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class SalesOrderLine : BaseEntity
{
    public int LineNumber { get; set; }

    public string? OrderNo { get; set; }

    public string? Sku { get; set; }
    public string? LinkSku { get; set; }

    public string? CustomerSKU { get; set; }
    public string? CustomerPdDescription { get; set; }
    public string? CustomerPdFinish { get; set; }
    public string? CustomerPdFeatures { get; set; }

    public decimal? RMBPrice { get; set; }
    public decimal? USDPrice { get; set; }
    public decimal? QTY { get; set; }

    public decimal? RMBAmount { get; set; }
    public decimal? USDAmount { get; set; }

    public decimal? PcsOfCarton { get; set; }
    public decimal? NwOfCarton { get; set; }
    public decimal? GwOfCarton { get; set; }
    public decimal? CbmOfCarton { get; set; }

    public decimal? Ctns { get; set; }
    public decimal? NwSubTotal { get; set; }
    public decimal? GwSubTotal { get; set; }
    public decimal? CbmSubTotal { get; set; }

    public string? SubNote { get; set; }

    public decimal? ShippedQTY { get; set; }
    public decimal? UnshippedQty { get; set; }
    public bool IsPartiallyShipped { get; set; }
    public bool IsFullyShipped { get; set; }
    public DateTime? LastShippedDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? MainImage { get; set; }

    public DateTime? ChangeTime { get; set; }
}

