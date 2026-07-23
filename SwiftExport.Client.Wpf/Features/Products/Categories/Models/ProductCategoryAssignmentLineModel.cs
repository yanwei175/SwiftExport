using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Features.Products.Categories.Models;

public sealed partial class ProductCategoryAssignmentLineModel : ObservableObject
{
    [ObservableProperty]
    public partial Guid Uid { get; set; }

    [ObservableProperty]
    public partial int Version { get; set; }

    [ObservableProperty]
    public partial string? MainImage { get; set; }

    [ObservableProperty]
    public partial string Sku { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? LinkSku { get; set; }

    [ObservableProperty]
    public partial string? DescriptionZh { get; set; }

    [ObservableProperty]
    public partial Guid OriginalCategoryId { get; set; }

    [ObservableProperty]
    public partial string OriginalCategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public static ProductCategoryAssignmentLineModel FromDto(ProductCategoryAssignmentRowDto dto) => new()
    {
        Uid = dto.Uid,
        Version = dto.Version,
        MainImage = dto.MainImage,
        Sku = dto.Sku ?? string.Empty,
        LinkSku = dto.LinkSku,
        DescriptionZh = dto.DescriptionZh,
        OriginalCategoryId = dto.OriginalCategoryId,
        OriginalCategoryName = dto.OriginalCategoryName ?? string.Empty
    };
}
