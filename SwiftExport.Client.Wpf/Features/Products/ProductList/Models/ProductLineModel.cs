using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

public sealed partial class ProductLineModel : BaseLineModel<ProductDto>
{
    [ObservableProperty] private Guid uid;
    [ObservableProperty] private int version;
    [ObservableProperty] private bool isDeleted;
    [ObservableProperty] private DateTime? createdDate;
    [ObservableProperty] private string? createdBy;
    [ObservableProperty] private DateTime? updatedDate;
    [ObservableProperty] private string? updatedBy;
    [ObservableProperty] private string sku = string.Empty;
    [ObservableProperty] private string masterSku = string.Empty;
    [ObservableProperty] private string descriptionEn = string.Empty;
    [ObservableProperty] private string descriptionZh = string.Empty;
    [ObservableProperty] private string featuresEn = string.Empty;
    [ObservableProperty] private string featuresZh = string.Empty;
    [ObservableProperty] private string finish = string.Empty;
    [ObservableProperty] private string materialType = string.Empty;
    [ObservableProperty] private string supplyType = string.Empty;
    [ObservableProperty] private int bomVersion;
    [ObservableProperty] private string bomNo = string.Empty;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string linkSku = string.Empty;
    [ObservableProperty] private decimal cnyPrice;
    [ObservableProperty] private decimal usdPrice;
    [ObservableProperty] private decimal? pcsOfCarton;
    [ObservableProperty] private decimal? nwOfCarton;
    [ObservableProperty] private decimal? gwOfCarton;
    [ObservableProperty] private decimal? cbmOfCarton;
    [ObservableProperty] private Guid categoryId;
    [ObservableProperty] private Guid colorId;
    [ObservableProperty] private string categoryName = string.Empty;
    [ObservableProperty] private string supplierName = string.Empty;
    [ObservableProperty] private string defaultCustomerCode = string.Empty;
    [ObservableProperty] private string defaultCustomerSku = string.Empty;
    [ObservableProperty] private string defaultSupplierCode = string.Empty;
    [ObservableProperty] private string defaultSupplierSku = string.Empty;
    [ObservableProperty] private string mainImage = string.Empty;

    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku)) return Task.FromResult<string?>("SKU 不能为空。");
        if (BomVersion < 0) return Task.FromResult<string?>("BOM 版本不能小于 0。");
        if (CnyPrice < 0 || UsdPrice < 0) return Task.FromResult<string?>("价格不能小于 0。");
        return Task.FromResult<string?>(null);
    }

    protected override void LoadFromDto(ProductDto dto)
    {
        Uid = dto.Uid; Version = dto.Version; IsDeleted = dto.IsDeleted;
        CreatedDate = dto.CreatedDate; CreatedBy = dto.CreatedBy; UpdatedDate = dto.UpdatedDate; UpdatedBy = dto.UpdatedBy;
        Sku = dto.Sku ?? string.Empty; MasterSku = dto.MasterSku ?? string.Empty;
        DescriptionEn = dto.DescriptionEn ?? string.Empty; DescriptionZh = dto.DescriptionZh ?? string.Empty;
        FeaturesEn = dto.FeaturesEn ?? string.Empty; FeaturesZh = dto.FeaturesZh ?? string.Empty;
        Finish = dto.Finish ?? string.Empty; MaterialType = dto.MaterialType ?? string.Empty; SupplyType = dto.SupplyType ?? string.Empty;
        BomVersion = dto.BomVersion; BomNo = dto.BomNo ?? string.Empty; Note = dto.Note ?? string.Empty; LinkSku = dto.LinkSku ?? string.Empty;
        CnyPrice = dto.CNYPrice; UsdPrice = dto.USDPrice;
        PcsOfCarton = dto.PcsOfCarton; NwOfCarton = dto.NwOfCarton; GwOfCarton = dto.GwOfCarton; CbmOfCarton = dto.CbmOfCarton;
        CategoryId = dto.CategoryId; ColorId = dto.ColorId; CategoryName = dto.CategoryName ?? string.Empty; SupplierName = dto.SupplierName ?? string.Empty;
        DefaultCustomerCode = dto.DefaultCustomerCode ?? string.Empty; DefaultCustomerSku = dto.DefaultCustomerSku ?? string.Empty;
        DefaultSupplierCode = dto.DefaultSupplierCode ?? string.Empty; DefaultSupplierSku = dto.DefaultSupplierSku ?? string.Empty;
        MainImage = dto.MainImage ?? string.Empty;
    }

    public override ProductDto ToDto() => new()
    {
        Uid = Uid, Version = Version, IsDeleted = IsDeleted,
        CreatedDate = CreatedDate, CreatedBy = CreatedBy, UpdatedDate = UpdatedDate, UpdatedBy = UpdatedBy,
        Sku = Sku, MasterSku = MasterSku, DescriptionEn = DescriptionEn, DescriptionZh = DescriptionZh,
        FeaturesEn = FeaturesEn, FeaturesZh = FeaturesZh, Finish = Finish, MaterialType = MaterialType, SupplyType = SupplyType,
        BomVersion = BomVersion, BomNo = BomNo, Note = Note, LinkSku = LinkSku,
        CNYPrice = CnyPrice, USDPrice = UsdPrice,
        PcsOfCarton = PcsOfCarton, NwOfCarton = NwOfCarton, GwOfCarton = GwOfCarton, CbmOfCarton = CbmOfCarton,
        CategoryId = CategoryId, ColorId = ColorId, CategoryName = CategoryName, SupplierName = SupplierName,
        DefaultCustomerCode = DefaultCustomerCode, DefaultCustomerSku = DefaultCustomerSku,
        DefaultSupplierCode = DefaultSupplierCode, DefaultSupplierSku = DefaultSupplierSku, MainImage = MainImage,
    };
}
