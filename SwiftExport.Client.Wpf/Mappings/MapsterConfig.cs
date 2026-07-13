using Mapster;
using System.Runtime.CompilerServices;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.ProductList.Models;

namespace SwiftExportClient.Mappings;

public sealed class MapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductCategoryDto, ProductCategoryTreeItemModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.CategoryName, src => src.CategoryName ?? string.Empty)
            .Map(dest => dest.FakeName, src => src.FakeName ?? string.Empty)
            .Map(dest => dest.CategoryCode, src => src.CategoryCode ?? string.Empty);

        config.NewConfig<ProductCategoryTreeItemModel, ProductCategoryDto>()
            .Map(dest => dest.ParentId, src => src.ParentId == Guid.Empty ? null : src.ParentId)
            .Map(dest => dest.CategoryName, src => src.CategoryName.Trim())
            .Map(dest => dest.FakeName, src => src.FakeName.Trim())
            .Map(dest => dest.CategoryCode, src => src.CategoryCode.Trim());

        config.NewConfig<ProductDto, ProductLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted)
            .Map(dest => dest.CnyPrice, src => src.CNYPrice)
            .Map(dest => dest.UsdPrice, src => src.USDPrice);

        config.NewConfig<ProductLineModel, ProductDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted)
            .Map(dest => dest.CNYPrice, src => src.CnyPrice)
            .Map(dest => dest.USDPrice, src => src.UsdPrice);

        config.NewConfig<ProductColorDto, ProductColorLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<ProductColorLineModel, ProductColorDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);

        config.NewConfig<BomDto, BomLineModel>()
            .IgnoreNullValues(true)
            .Map(dest => dest.PersistedIsDeleted, src => src.IsDeleted);

        config.NewConfig<BomLineModel, BomDto>()
            .Map(dest => dest.IsDeleted, src => src.PersistedIsDeleted);
    }
}

internal static class MapsterInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        new MapsterConfig().Register(TypeAdapterConfig.GlobalSettings);
    }
}
