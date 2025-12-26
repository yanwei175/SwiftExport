using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using SwiftExport.Core.Entities;
namespace SwiftExport.AppLayer.DTOs
{
    public class ProductProfiles : Profile
    {
        public ProductProfiles()
        {
            CreateMap<Product, ProductDto>()
                .ReverseMap();
        }



    }
}
