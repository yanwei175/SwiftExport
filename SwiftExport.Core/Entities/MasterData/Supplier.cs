using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class Supplier : BaseEntity
{
    public string? SupplierCode { get; set; } = default!;
    public string? SupplierName { get; set; }
    public decimal? ZheKou { get; set; }
    public string? ZheKouBeiZhu { get; set; }
    public string? DocRelateNo { get; set; }
}
