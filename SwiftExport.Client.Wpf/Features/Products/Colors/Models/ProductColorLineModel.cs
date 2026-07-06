using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.Colors.Models;

public sealed class ProductColorLineModel : BaseLineModel<ProductColorDto>
{
    private Guid _uid;
    private int _version;
    private bool _isDeleted;
    private DateTime? _createdDate;
    private string? _createdBy;
    private DateTime? _updatedDate;
    private string? _updatedBy;
    private string _colorCodeUnique = string.Empty;
    private string _colorCode = string.Empty;
    private string _colorNameEn = string.Empty;
    private string _colorNameZh = string.Empty;

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

    public string ColorCodeUnique
    {
        get => _colorCodeUnique;
        set => SetTrackedProperty(ref _colorCodeUnique, value);
    }

    public string ColorCode
    {
        get => _colorCode;
        set => SetTrackedProperty(ref _colorCode, value);
    }

    public string ColorNameEn
    {
        get => _colorNameEn;
        set => SetTrackedProperty(ref _colorNameEn, value);
    }

    public string ColorNameZh
    {
        get => _colorNameZh;
        set => SetTrackedProperty(ref _colorNameZh, value);
    }

    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ColorCodeUnique))
        {
            return Task.FromResult<string?>("唯一颜色代码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(ColorCode))
        {
            return Task.FromResult<string?>("颜色代码不能为空。");
        }

        return Task.FromResult<string?>(null);
    }

    protected override void LoadFromDto(ProductColorDto dto)
    {
        Uid = dto.Uid;
        Version = dto.Version;
        _isDeleted = dto.IsDeleted;
        _createdDate = dto.CreatedDate;
        _createdBy = dto.CreatedBy;
        _updatedDate = dto.UpdatedDate;
        _updatedBy = dto.UpdatedBy;
        ColorCodeUnique = dto.ColorCodeUnique ?? string.Empty;
        ColorCode = dto.ColorCode ?? string.Empty;
        ColorNameEn = dto.ColorNameEn ?? string.Empty;
        ColorNameZh = dto.ColorNameZh ?? string.Empty;
    }

    public override ProductColorDto ToDto()
    {
        return new ProductColorDto
        {
            Uid = Uid,
            Version = Version,
            IsDeleted = _isDeleted,
            CreatedDate = _createdDate,
            CreatedBy = _createdBy,
            UpdatedDate = _updatedDate,
            UpdatedBy = _updatedBy,
            ColorCodeUnique = ColorCodeUnique,
            ColorCode = ColorCode,
            ColorNameEn = ColorNameEn,
            ColorNameZh = ColorNameZh,
        };
    }
}
