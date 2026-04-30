using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

[Table("Suppliers")]
public class Supplier : BaseEntity
{
    public required string SupplierCode { get; set; }
    public required string SupplierName { get; set; }
    public decimal? ZheKou { get; set; }
    public string? ZheKouBeiZhu { get; set; }
    public required string DocRelateNo { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Currency { get; set; } = "CNY";
    public decimal? TaxRate { get; set; }
    public string? PaymentTerms { get; set; }
    public int? DefaultLeadTime { get; set; }
}
