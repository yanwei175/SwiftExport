using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// Packing List line. Each line represents finished SKU quantity and keeps source PO/SO line references.
/// </summary>
[Table("PackingListLine")]
public class PackingListLine : BaseEntity
{
    public int LineNumber { get; set; } = 1;
    public required string PackingListNo { get; set; }
    public required Guid PackingListHeadId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceNo { get; set; }
    public Guid? SourceLineId { get; set; }
    public Guid? DependentDemandId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public string? CustomerSku { get; set; }
    public required string Sku { get; set; }
    public required decimal Qty { get; set; } = 0;
    public string? ContainerNo { get; set; }
    public string? SealNo { get; set; }
    public string? InvoiceNo { get; set; }
    public string? SalesOrderNo { get; set; }



    public string? DescriptionEn { get; set; }
    public string? FinishEn { get; set; }
    public int PcsPerPackage { get; set; } = 1;
    public decimal? Nw_KG { get; set; }
    public decimal? Gw_KG { get; set; }
    public decimal? Cbm_m3 { get; set; }
    public decimal? TotalCtns { get; set; }
    public decimal? TotalNw_KG { get; set; }
    public decimal? TotalGw_KG { get; set; }
    public decimal? TotalCbm_m3 { get; set; }

    public string? Unit { get; set; }
    public string? Remark { get; set; }



}
