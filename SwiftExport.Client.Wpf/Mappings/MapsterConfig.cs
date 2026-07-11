using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Categories.Models;

namespace SwiftExportClient.Mappings;

public sealed class MapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductCategoryDto, ProductCategoryTreeItemModel>()
            .Map(dest => dest.CategoryName, src => src.CategoryName ?? string.Empty)
            .Map(dest => dest.FakeName, src => src.FakeName ?? string.Empty)
            .Map(dest => dest.CategoryCode, src => src.CategoryCode ?? string.Empty);

        config.NewConfig<ProductCategoryTreeItemModel, ProductCategoryDto>()
            .Map(dest => dest.ParentId, src => src.ParentId == Guid.Empty ? null : src.ParentId)
            .Map(dest => dest.CategoryName, src => src.CategoryName.Trim())
            .Map(dest => dest.FakeName, src => src.FakeName.Trim())
            .Map(dest => dest.CategoryCode, src => src.CategoryCode.Trim());
    }
}
