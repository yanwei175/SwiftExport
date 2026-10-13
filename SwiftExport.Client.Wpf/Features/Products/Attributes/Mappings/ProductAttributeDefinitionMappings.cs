using Mapster;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExportClient.Features.Products.Attributes.Models;

namespace SwiftExportClient.Features.Products.Attributes.Mappings;

/// <summary>集中定义标签维护 DTO、私有行模型和保存字段之间的结构映射。</summary>
public sealed class ProductAttributeDefinitionMappings : IRegister
{
    /// <inheritdoc/>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductAttributeGroupDto, ProductAttributeGroupLineModel>()
            .Map(dest => dest.GroupCode, src => src.GroupCode ?? string.Empty)
            .Map(dest => dest.GroupName, src => src.GroupName ?? string.Empty);
        config.NewConfig<ProductAttributeOptionDto, ProductAttributeOptionLineModel>()
            .Map(dest => dest.OptionCode, src => src.OptionCode ?? string.Empty)
            .Map(dest => dest.OptionName, src => src.OptionName ?? string.Empty);
        config.NewConfig<ProductAttributeGroupLineModel, ProductAttributeGroupDto>().Ignore(dest => dest.Options);
        config.NewConfig<ProductAttributeOptionLineModel, ProductAttributeOptionDto>();
        config.NewConfig<ProductAttributeGroupLineModel, ProductAttributeGroupWriteDto>()
            .Map(dest => dest.ClientUid, src => src.Uid)
            .Map(dest => dest.GroupCode, src => src.GroupCode.Trim())
            .Map(dest => dest.GroupName, src => src.GroupName.Trim());
        config.NewConfig<ProductAttributeOptionLineModel, ProductAttributeOptionWriteDto>()
            .Map(dest => dest.OptionCode, src => src.OptionCode.Trim())
            .Map(dest => dest.OptionName, src => src.OptionName.Trim());
    }
}

