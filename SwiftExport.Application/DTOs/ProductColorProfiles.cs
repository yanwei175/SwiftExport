using AutoMapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class ProductColorProfiles : Profile
    {
        public ProductColorProfiles()
        {
            CreateMap<ProductColorDto, ProductColor>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.ColorCodeUnique, opt => opt.MapFrom(src => src.唯一颜色代码))
                .ForMember(dest => dest.ColorCode, opt => opt.MapFrom(src => src.颜色代码))
                .ForMember(dest => dest.ColorNameEn, opt => opt.MapFrom(src => src.英文颜色名称))
                .ForMember(dest => dest.ColorNameZh, opt => opt.MapFrom(src => src.中文颜色名称))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.ChangeTime, opt => opt.MapFrom(src => src.修改时间))
                .ReverseMap();

        }

    }
}
