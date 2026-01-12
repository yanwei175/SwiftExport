using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Relations;

public class CustomerPoConfig : BaseEntity
{
    public string? CustomerCode { get; set; }
    public string? SupplierCode { get; set; }
    public string? InletHoseType { get; set; }
    public string? AeratorType { get; set; }
    public string? PalletType { get; set; }
    public string? BoxType { get; set; }
    public string? LabelType { get; set; }
    public string? IMType { get; set; }
    public string? CartonType { get; set; }
    public string? LogoType { get; set; }
    public string? SalesPerson { get; set; }
    public string? Purchaser { get; set; }
    public string? Note { get; set; }

}

