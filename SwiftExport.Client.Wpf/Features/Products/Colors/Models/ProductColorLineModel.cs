using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Colors.Models;

public sealed partial class ProductColorLineModel : BaseLineModel<ProductColorDto>, IDataErrorInfo
{
    [ObservableProperty]
    private Guid uid;

    [ObservableProperty]
    private int version;

    private bool _isDeleted;
    private DateTime? _createdDate;
    private string? _createdBy;
    private DateTime? _updatedDate;
    private string? _updatedBy;

    [ObservableProperty]
    private string colorCodeUnique = string.Empty;

    [ObservableProperty]
    private string colorCode = string.Empty;

    [ObservableProperty]
    private string colorNameEn = string.Empty;

    [ObservableProperty]
    private string colorNameZh = string.Empty;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(ColorCodeUnique) when string.IsNullOrWhiteSpace(ColorCodeUnique) => "唯一颜色代码不能为空。",
        nameof(ColorCode) when string.IsNullOrWhiteSpace(ColorCode) => "颜色代码不能为空。",
        _ => string.Empty
    };

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
