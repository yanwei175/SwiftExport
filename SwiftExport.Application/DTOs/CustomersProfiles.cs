using AutoMapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class CustomersProfiles : Profile
    {
        public CustomersProfiles()
        {
            CreateMap<CustomersDto, Customers>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.CustomerCode, opt => opt.MapFrom(src => src.客户代码))
                .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.客户名称))
                .ForMember(dest => dest.CustomerAddress, opt => opt.MapFrom(src => src.地址))
                .ForMember(dest => dest.CustomerPhone, opt => opt.MapFrom(src => src.联系方式))
                .ForMember(dest => dest.CustomerContact, opt => opt.MapFrom(src => src.联系人))
                .ForMember(dest => dest.LabelFolder, opt => opt.MapFrom(src => src.标贴文件夹))
                .ForMember(dest => dest.IMFolder, opt => opt.MapFrom(src => src.说明书文件夹))
                .ForMember(dest => dest.DrawingFolder, opt => opt.MapFrom(src => src.尺寸图文件夹))
                .ForMember(dest => dest.LinedrawingFolder, opt => opt.MapFrom(src => src.线框图文件夹))
                .ForMember(dest => dest.BarcodeFolder, opt => opt.MapFrom(src => src.条码文件夹))
                .ForMember(dest => dest.SpecSheetFolder, opt => opt.MapFrom(src => src.规格书文件夹))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.ChangeTime, opt => opt.MapFrom(src => src.修改时间))
                .ForMember(dest => dest.DocRelateNo, opt => opt.MapFrom(src => src.单据关联))
                .ReverseMap();

        }

    }
}
