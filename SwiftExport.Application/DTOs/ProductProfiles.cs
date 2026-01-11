using AutoMapper;
using SwiftExport.Core.Entities;
namespace SwiftExport.AppLayer.DTOs
{
    public class ProductProfiles : Profile
    {
        public ProductProfiles()
        {
            CreateMap<ProductDto, Product>()
                .ForMember(dest => dest.Even, opt => opt.MapFrom(src => src.Even))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.SKU, opt => opt.MapFrom(src => src.SKU))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.描述))
                .ForMember(dest => dest.Finish, opt => opt.MapFrom(src => src.颜色))
                .ForMember(dest => dest.Features, opt => opt.MapFrom(src => src.配置))
                .ForMember(dest => dest.SupplyType, opt => opt.MapFrom(src => src.供货类型))
                .ForMember(dest => dest.BomNo, opt => opt.MapFrom(src => src.BOM编号))
                .ForMember(dest => dest.Class, opt => opt.MapFrom(src => src.类别))
                .ForMember(dest => dest.Note, opt => opt.MapFrom(src => src.备注))
                .ForMember(dest => dest.LinkSku, opt => opt.MapFrom(src => src.关联SKU))
                .ForMember(dest => dest.MainImage, opt => opt.MapFrom(src => src.主图))
                .ForMember(dest => dest.CustomerCode, opt => opt.MapFrom(src => src.客户代码))
                .ForMember(dest => dest.CSKU, opt => opt.MapFrom(src => src.客户SKU))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.供应商代码))
                .ForMember(dest => dest.CSKU, opt => opt.MapFrom(src => src.供应商SKU))
                .ForMember(dest => dest.ProductCategory, opt => opt.MapFrom(src => src.产品分类))
                .ReverseMap();
        }



    }
}
