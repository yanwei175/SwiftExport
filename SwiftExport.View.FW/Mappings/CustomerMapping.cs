using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.View.FW.CustomerViews.Models;

namespace SwiftExport.View.FW.Mappings
{
    /// <summary>
    /// 客户 DTO ↔ Model 映射配置
    /// 使用 Mapster 自动映射，支持双向转换
    /// </summary>
    public class CustomerMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // DTO → Model 映射
            config.NewConfig<CustomerDto, CustomerModel>()
                // 字段直接映射（属性名相同）
                .Map(dest => dest.CustomerCode, src => src.CustomerCode)
                .Map(dest => dest.CustomerName, src => src.CustomerName)
                .Map(dest => dest.Address, src => src.Address)
                .Map(dest => dest.PhoneNumber, src => src.PhoneNumber)
                .Map(dest => dest.ContactPerson, src => src.ContactPerson)
                .Map(dest => dest.DocRelateNo, src => src.DocRelateNo)

                // 文件夹路径字段映射
                .Map(dest => dest.LabelFolderPath, src => src.LabelFolderPath)
                .Map(dest => dest.ManualFolderPath, src => src.ManualFolderPath)
                .Map(dest => dest.DrawingFolderPath, src => src.DrawingFolderPath)
                .Map(dest => dest.LinedrawingFolderPath, src => src.LinedrawingFolderPath)
                .Map(dest => dest.BarcodeFolderPath, src => src.BarcodeFolderPath)
                .Map(dest => dest.SpecSheetFolderPath, src => src.SpecSheetFolderPath)

                // 贸易条款默认值字段映射
                .Map(dest => dest.IncotermSortRemark, src => src.IncotermSortRemark)
                .Map(dest => dest.Incoterm, src => src.Incoterm)
                .Map(dest => dest.IncotermLocation, src => src.IncotermLocation)
                .Map(dest => dest.DepositRate, src => src.DepositRate)
                .Map(dest => dest.LeadTimeDays, src => src.LeadTimeDays)
                .Map(dest => dest.PaymentMethod, src => src.PaymentMethod)
                .Map(dest => dest.Currency, src => src.Currency)
                .Map(dest => dest.EtdSortRemark, src => src.EtdSortRemark)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)

                // 基类字段映射（由 BaseDto 提供）
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Version, src => src.Version)
                .IgnoreNullValues(true);

            // Model → DTO 映射（逆向）
            config.NewConfig<CustomerModel, CustomerDto>()
                .Map(dest => dest.CustomerCode, src => src.CustomerCode)
                .Map(dest => dest.CustomerName, src => src.CustomerName)
                .Map(dest => dest.Address, src => src.Address)
                .Map(dest => dest.PhoneNumber, src => src.PhoneNumber)
                .Map(dest => dest.ContactPerson, src => src.ContactPerson)
                .Map(dest => dest.DocRelateNo, src => src.DocRelateNo)

                .Map(dest => dest.LabelFolderPath, src => src.LabelFolderPath)
                .Map(dest => dest.ManualFolderPath, src => src.ManualFolderPath)
                .Map(dest => dest.DrawingFolderPath, src => src.DrawingFolderPath)
                .Map(dest => dest.LinedrawingFolderPath, src => src.LinedrawingFolderPath)
                .Map(dest => dest.BarcodeFolderPath, src => src.BarcodeFolderPath)
                .Map(dest => dest.SpecSheetFolderPath, src => src.SpecSheetFolderPath)

                .Map(dest => dest.IncotermSortRemark, src => src.IncotermSortRemark)
                .Map(dest => dest.Incoterm, src => src.Incoterm)
                .Map(dest => dest.IncotermLocation, src => src.IncotermLocation)
                .Map(dest => dest.DepositRate, src => src.DepositRate)
                .Map(dest => dest.LeadTimeDays, src => src.LeadTimeDays)
                .Map(dest => dest.PaymentMethod, src => src.PaymentMethod)
                .Map(dest => dest.Currency, src => src.Currency)
                .Map(dest => dest.EtdSortRemark, src => src.EtdSortRemark)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)

                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Version, src => src.Version)
                .IgnoreNullValues(true);
        }
    }
}
