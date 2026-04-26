namespace SwiftExport.Core.ReadModels.Products;

public class ProductPagedItem
{
    public int Id { get; set; }
    public string? MasterSku { get; set; }
    public string Sku { get; set; } = default!;
    public string? DescriptionEn { get; set; }
    public string? DescriptionZh { get; set; }
    public int ColorId { get; set; }
    public string? Finish { get; set; }
    public string? FeaturesEn { get; set; }
    public string? FeaturesZh { get; set; }
    public string? MaterialType { get; set; }
    public string? SupplyType { get; set; }
    public string? BomNo { get; set; }
    public string? Note { get; set; }
    public string? LinkSku { get; set; }
    public string? MainImage { get; set; }
    public string? CustomerCode { get; set; }
    public string? CSku { get; set; }
    public string? SupplierCode { get; set; }
    public string? SSku { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public string? FakeName { get; set; }
    public string? ColorCodeUnique { get; set; }
    public string? ColorCode { get; set; }
    public string? ColorNameEn { get; set; }
}
