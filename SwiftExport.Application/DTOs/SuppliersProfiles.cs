using AutoMapper;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class SuppliersProfiles : Profile
    {
        public SuppliersProfiles()
        {
            CreateMap<SuppliersDto, Suppliers>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.供应商代码))
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.供应商名称))
                .ForMember(dest => dest.ZheKou, opt => opt.MapFrom(src => src.折扣))
                .ForMember(dest => dest.ZheKouBeiZhu, opt => opt.MapFrom(src => src.折扣备注))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.ChangeTime, opt => opt.MapFrom(src => src.修改时间))
                .ReverseMap();
        }

    }
}
