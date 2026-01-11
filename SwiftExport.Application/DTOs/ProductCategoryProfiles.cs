using AutoMapper;
using SwiftExport.Core.Entities;

namespace SwiftExport.AppLayer.DTOs
{
    public class ProductCategoryDtoProfiles : Profile
    {
        public ProductCategoryDtoProfiles()
        {
            CreateMap<ProductCategoryDto, ProductCategory>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.分类名称))
                .ForMember(dest => dest.ParentID, opt => opt.MapFrom(src => src.父级ID))
                .ForMember(dest => dest.ParentName, opt => opt.MapFrom(src => src.父级名称))
                .ForMember(dest => dest.SortNo, opt => opt.MapFrom(src => src.排序编号))
                .ForMember(dest => dest.CategoryNo, opt => opt.MapFrom(src => src.分类编号))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.ChangeTime, opt => opt.MapFrom(src => src.修改时间))
                .ReverseMap();

        }

    }
}
