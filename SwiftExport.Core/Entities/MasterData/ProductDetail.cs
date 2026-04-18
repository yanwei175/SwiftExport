using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductDetail : BaseEntity
{
    public string ProductSKU { get; set; } = default!;
    public string? LabelPath { get; set; }
    public string? IMPath { get; set; }
    public string? DrawingPath { get; set; }
    public string? LinedrawingPath { get; set; }
    public string? SpecSheetPath { get; set; }
    public string? Barcode1Path { get; set; }
    public string? Barcode2Path { get; set; }

}
