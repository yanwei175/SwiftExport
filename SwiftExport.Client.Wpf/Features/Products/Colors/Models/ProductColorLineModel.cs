using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.Colors.Models;

public sealed partial class ProductColorLineModel : BaseLineModel<ProductColorDto>, IDataErrorInfo
{
    [ObservableProperty]
    public partial string ColorCodeUnique { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorNameEn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorNameZh { get; set; } = string.Empty;

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
        dto.Adapt(this);
    }

    public override ProductColorDto ToDto() => this.Adapt<ProductColorDto>();
}
