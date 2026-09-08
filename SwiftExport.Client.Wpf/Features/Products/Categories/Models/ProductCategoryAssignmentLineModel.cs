using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;

namespace SwiftExportClient.Features.Products.Categories.Models;

/// <summary>分类管理页中可勾选的主型号归类行。</summary>
public sealed partial class ProductCategoryAssignmentLineModel : ObservableObject
{
    [ObservableProperty]
    public partial Guid Uid { get; set; }

    [ObservableProperty]
    public partial int Version { get; set; }

    [ObservableProperty]
    public partial string? MainImagePath { get; set; }

    [ObservableProperty]
    public partial string MasterSku { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? DefaultDescriptionZh { get; set; }

    [ObservableProperty]
    public partial Guid OriginalCategoryId { get; set; }

    [ObservableProperty]
    public partial string OriginalCategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>把主型号分页投影转换为页面勾选行。</summary>
    public static ProductCategoryAssignmentLineModel FromDto(ProductMasterListItemDto dto) =>
        dto.Adapt<ProductCategoryAssignmentLineModel>();
}
