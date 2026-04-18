using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.View.FW.CustomerViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mappings
{
    public class CustomerProductMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // DTO → Model
            config.NewConfig<CustomerProductDto, CustomerProductModel>()
                .Map(dest => dest.CategoryId, dto => dto.CategoryId ?? 0)
                .Map(dest => dest.LeadTimeDays, dto => dto.LeadTimeDays ?? 0)
                .Map(dest => dest.MOQ, dto => dto.MOQ ?? 0)
                .Map(dest => dest.Price, dto => dto.Price ?? 0)
                .TwoWays();

            // Model → DTO
            config.NewConfig<CustomerProductModel, CustomerProductDto>()
                .Map(dto => dto.CategoryId, src => src.CategoryId == 0 ? (int?)null : src.CategoryId)
                .Map(dto => dto.LeadTimeDays, src => src.LeadTimeDays == 0 ? (int?)null : src.LeadTimeDays)
                .Map(dto => dto.MOQ, src => src.MOQ == 0 ? (int?)null : src.MOQ)
                // Price 不需要变 null
                .TwoWays();


        }
    }
}
