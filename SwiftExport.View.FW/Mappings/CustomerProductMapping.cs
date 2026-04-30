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
            // 将 DTO（可空类型）映射到 Model（保持可空以支持 UI 绑定）
            config.NewConfig<CustomerProductDto, CustomerProductModel>()
                // 外键字段 - 直接映射
                .Map(dest => dest.CustomerId, dto => dto.CustomerId)
                .Map(dest => dest.CustomerCode, dto => dto.CustomerCode)
                .Map(dest => dest.SkuId, dto => dto.SkuId)
                .Map(dest => dest.Sku, dto => dto.Sku)
                .Map(dest => dest.LinkSku, dto => dto.LinkSku)
                .Map(dest => dest.Description, dto => dto.Description)
                .Map(dest => dest.CategoryName, dto => dto.CategoryName)
                .Map(dest => dest.MaterialType, dto => dto.MaterialType)
                // 客户自定义字段 - 直接映射
                .Map(dest => dest.CustomerSku, dto => dto.CustomerSku)
                .Map(dest => dest.CustomerPdDescription, dto => dto.CustomerPdDescription)
                .Map(dest => dest.CustomerPdSeries, dto => dto.CustomerPdSeries)
                .Map(dest => dest.CustomerColorName, dto => dto.CustomerColorName)
                .Map(dest => dest.CustomerPdFeatures, dto => dto.CustomerPdFeatures)
                .Map(dest => dest.CustomerBarcode1, dto => dto.CustomerBarcode1)
                .Map(dest => dest.CustomerBarcode2, dto => dto.CustomerBarcode2)
                .Map(dest => dest.CustomerSkuUnit, dto => dto.CustomerSkuUnit)
                // 价格相关 - 直接映射（保持可空）
                .Map(dest => dest.USDPrice, dto => dto.USDPrice)
                .Map(dest => dest.CNYPrice, dto => dto.CNYPrice)
                .Map(dest => dest.Currency, dto => dto.Currency)
                // 业务要求 - 直接映射（保持可空）
                .Map(dest => dest.LeadTimeDays, dto => dto.LeadTimeDays)
                .Map(dest => dest.MOQ, dto => dto.MOQ)
                // 状态字段 - 直接映射
                .Map(dest => dest.IsActive, dto => dto.IsActive)
                .Map(dest => dest.IsDeleted, dto => dto.IsDeleted)
                .TwoWays();

        }
    }
}
