
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class SalesOrderLine : BaseEntity
{
    public int LineNumber { get; set; }
    public required int SalesOrderId { get; set; }
    public string? OrderNo { get; set; }

    public string? Sku { get; set; }
    public string? LinkSku { get; set; }

    public string? CustomerSku { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ColorNameEn { get; set; }
    public string? FeaturesEn { get; set; }

    public decimal? CNYPrice { get; set; }
    public decimal? USDPrice { get; set; }
    public decimal? QTY { get; set; }

    public decimal? CNYAmount { get; set; }
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

    /// <summary>
    /// 最后一次发货日期
    /// </summary>
    public DateTime? LastShippedDate { get; set; }
    /// <summary>
    /// 预计交货日期
    /// </summary>
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? MainImage { get; set; }

    public DateTime? ChangeTime { get; set; }
}

