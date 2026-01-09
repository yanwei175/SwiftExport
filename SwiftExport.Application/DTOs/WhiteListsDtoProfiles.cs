using AutoMapper;
using SwiftExport.Core.Entities;

namespace SwiftExport.AppLayer.DTOs
{
    public class WhiteListsDtoProfiles : Profile
    {
        public WhiteListsDtoProfiles()
        {
            CreateMap<WhiteListsDto, WhiteLists>()
                .ForMember(dest => dest.Even, opt => opt.MapFrom(src => src.Even))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.ExcelWhiteList, opt => opt.MapFrom(src => src.Excel白名单))
                .ReverseMap();
        }

    }
}
