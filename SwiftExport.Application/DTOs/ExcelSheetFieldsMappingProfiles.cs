using AutoMapper;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class ExcelSheetFieldsMappingProfiles : Profile
    {
        public ExcelSheetFieldsMappingProfiles()
        {
            CreateMap<ExcelSheetFieldsMappingDto, ExcelSheetFieldsMapping>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.ShengXiaoDomain, opt => opt.MapFrom(src => src.生效表))
                .ForMember(dest => dest.SourceFields, opt => opt.MapFrom(src => src.源字段))
                .ForMember(dest => dest.TargetFields, opt => opt.MapFrom(src => src.目标字段))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.创建时间))
                .ForMember(dest => dest.ChangeTime, opt => opt.MapFrom(src => src.修改时间))
                .ReverseMap()
                ;
        }
         
    }
}
