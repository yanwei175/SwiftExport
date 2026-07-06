using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

public sealed class ProductLineModel : BaseLineModel<ProductDto>
{
    private Guid _uid;
    private int _version;
    private bool _isDeleted;
    private DateTime? _createdDate;
    private string? _createdBy;
    private DateTime? _updatedDate;
    private string? _updatedBy;
    private Guid _categoryId;
    private Guid _colorId;
    private string? _featuresEn;
    private string? _featuresZh;
    private string? _finish;
    private string? _linkSku;
    private decimal? _pcsOfCarton;
    private decimal? _nwOfCarton;
    private decimal? _gwOfCarton;
    private decimal? _cbmOfCarton;
    private string? _supplierName;
    private string? _defaultCustomerCode;
    private string? _defaultCustomerSku;
    private string? _defaultSupplierCode;
    private string? _defaultSupplierSku;
    private string? _mainImage;
    private string _sku = string.Empty;
    private string _masterSku = string.Empty;
    private string _descriptionEn = string.Empty;
    private string _descriptionZh = string.Empty;
    private string _categoryName = string.Empty;
    private string _materialType = string.Empty;
    private string _supplyType = string.Empty;
    private int _bomVersion;
    private string _bomNo = string.Empty;
    private decimal _cnyPrice;
    private decimal _usdPrice;
    private string _note = string.Empty;

    public Guid Uid
    {
        get => _uid;
        set => SetProperty(ref _uid, value);
    }

    public int Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string Sku
    {
        get => _sku;
        set => SetTrackedProperty(ref _sku, value);
    }

    public string MasterSku
    {
        get => _masterSku;
        set => SetTrackedProperty(ref _masterSku, value);
    }

    public string DescriptionEn
    {
        get => _descriptionEn;
        set => SetTrackedProperty(ref _descriptionEn, value);
    }

    public string DescriptionZh
    {
        get => _descriptionZh;
        set => SetTrackedProperty(ref _descriptionZh, value);
    }

    public string CategoryName
    {
        get => _categoryName;
        set => SetTrackedProperty(ref _categoryName, value);
    }

    public string MaterialType
    {
        get => _materialType;
        set => SetTrackedProperty(ref _materialType, value);
    }

    public string SupplyType
    {
        get => _supplyType;
        set => SetTrackedProperty(ref _supplyType, value);
    }

    public int BomVersion
    {
        get => _bomVersion;
        set => SetTrackedProperty(ref _bomVersion, value);
    }

    public string BomNo
    {
        get => _bomNo;
        set => SetTrackedProperty(ref _bomNo, value);
    }

    public decimal CnyPrice
    {
        get => _cnyPrice;
        set => SetTrackedProperty(ref _cnyPrice, value);
    }

    public decimal UsdPrice
    {
        get => _usdPrice;
        set => SetTrackedProperty(ref _usdPrice, value);
    }

    public string Note
    {
        get => _note;
        set => SetTrackedProperty(ref _note, value);
    }

    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku))
        {
            return Task.FromResult<string?>("SKU 不能为空。");
        }

        if (BomVersion < 0)
        {
            return Task.FromResult<string?>("BOM 版本不能小于 0。");
        }

        if (CnyPrice < 0 || UsdPrice < 0)
        {
            return Task.FromResult<string?>("价格不能小于 0。");
        }

        return Task.FromResult<string?>(null);
    }

    protected override void LoadFromDto(ProductDto dto)
    {
        Uid = dto.Uid;
        Version = dto.Version;
        _isDeleted = dto.IsDeleted;
        _createdDate = dto.CreatedDate;
        _createdBy = dto.CreatedBy;
        _updatedDate = dto.UpdatedDate;
        _updatedBy = dto.UpdatedBy;
        _categoryId = dto.CategoryId;
        _colorId = dto.ColorId;
        _featuresEn = dto.FeaturesEn;
        _featuresZh = dto.FeaturesZh;
        _finish = dto.Finish;
        _linkSku = dto.LinkSku;
        _pcsOfCarton = dto.PcsOfCarton;
        _nwOfCarton = dto.NwOfCarton;
        _gwOfCarton = dto.GwOfCarton;
        _cbmOfCarton = dto.CbmOfCarton;
        _supplierName = dto.SupplierName;
        _defaultCustomerCode = dto.DefaultCustomerCode;
        _defaultCustomerSku = dto.DefaultCustomerSku;
        _defaultSupplierCode = dto.DefaultSupplierCode;
        _defaultSupplierSku = dto.DefaultSupplierSku;
        _mainImage = dto.MainImage;
        Sku = dto.Sku ?? string.Empty;
        MasterSku = dto.MasterSku ?? string.Empty;
        DescriptionEn = dto.DescriptionEn ?? string.Empty;
        DescriptionZh = dto.DescriptionZh ?? string.Empty;
        CategoryName = dto.CategoryName ?? string.Empty;
        MaterialType = dto.MaterialType ?? string.Empty;
        SupplyType = dto.SupplyType ?? string.Empty;
        BomVersion = dto.BomVersion;
        BomNo = dto.BomNo ?? string.Empty;
        CnyPrice = dto.CNYPrice;
        UsdPrice = dto.USDPrice;
        Note = dto.Note ?? string.Empty;
    }

    public override ProductDto ToDto()
    {
        return new ProductDto
        {
            Uid = Uid,
            Version = Version,
            IsDeleted = _isDeleted,
            CreatedDate = _createdDate,
            CreatedBy = _createdBy,
            UpdatedDate = _updatedDate,
            UpdatedBy = _updatedBy,
            CategoryId = _categoryId,
            ColorId = _colorId,
            Sku = Sku,
            MasterSku = MasterSku,
            DescriptionEn = DescriptionEn,
            DescriptionZh = DescriptionZh,
            FeaturesEn = _featuresEn,
            FeaturesZh = _featuresZh,
            Finish = _finish,
            CategoryName = CategoryName,
            MaterialType = MaterialType,
            SupplyType = SupplyType,
            BomVersion = BomVersion,
            BomNo = BomNo,
            CNYPrice = CnyPrice,
            USDPrice = UsdPrice,
            Note = Note,
            LinkSku = _linkSku,
            PcsOfCarton = _pcsOfCarton,
            NwOfCarton = _nwOfCarton,
            GwOfCarton = _gwOfCarton,
            CbmOfCarton = _cbmOfCarton,
            SupplierName = _supplierName,
            DefaultCustomerCode = _defaultCustomerCode,
            DefaultCustomerSku = _defaultCustomerSku,
            DefaultSupplierCode = _defaultSupplierCode,
            DefaultSupplierSku = _defaultSupplierSku,
            MainImage = _mainImage,
        };
    }
}
