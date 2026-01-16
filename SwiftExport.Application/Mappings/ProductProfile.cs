using AutoMapper;
using SwiftExport.Contracts.DTOs.Product;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Mappings;

public class ProductProfile : Profile
{
    public ProductProfile()
    {
        CreateMap<ProductDto, Product>()
            .ReverseMap()
            ;
    }
}