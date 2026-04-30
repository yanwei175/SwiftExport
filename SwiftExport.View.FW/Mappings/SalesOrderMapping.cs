using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.SalesOrderViews.Models;

namespace SwiftExport.View.FW.Mappings
{
    public class SalesOrderMapping : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            RegisterHeaderMapping(config);
            RegisterLineMapping(config);
        }

        /// <summary>
        /// 中文注释：注册销售订单头映射。
        /// 由于 HeaderDto 与 HeaderModel 现在已经定型，
        /// 这里直接按最终字段做双向映射。
        /// </summary>
        private static void RegisterHeaderMapping(TypeAdapterConfig config)
        {
            config.NewConfig<SalesOrderHeaderDto, SalesOrderHeaderModel>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.OrderStatus, src => src.OrderStatus)
                .Map(dest => dest.OrderNo, src => src.OrderNo)
                .Map(dest => dest.CustomerId, src => src.CustomerId)
                .Map(dest => dest.CustomerCode, src => src.CustomerCode)
                .Map(dest => dest.PoNo, src => src.CustomerPoNo)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)
                .Map(dest => dest.Currency, src => src.Currency)
                .Map(dest => dest.ExchangeRate, src => src.ExchangeRate)
                .Map(dest => dest.TotalAmountOriginalCurrency, src => src.TotalAmountOriginalCurrency)
                .Map(dest => dest.TotalAmountLocalCurrency, src => src.TotalAmountLocalCurrency)
                .Map(dest => dest.InvoiceNo, src => src.InvoiceNo)
                .Map(dest => dest.ShipToName, src => src.ShipToName)
                .Map(dest => dest.ShipToAddress, src => src.ShipToAddress)
                .Map(dest => dest.ShipToPhone, src => src.ShipToPhone)
                .Map(dest => dest.IncotermSortRemark, src => src.IncotermSortRemark)
                .Map(dest => dest.Incoterm, src => src.Incoterm)
                .Map(dest => dest.Incotermlocation, src => src.IncotermLocation)
                .Map(dest => dest.DepositRate, src => src.DepositRate)
                .Map(dest => dest.LeadTimeDays, src => src.LeadTimeDays)
                .Map(dest => dest.PaymentMethod, src => src.PaymentMethod)
                .Map(dest => dest.VersionNo, src => src.VersionNo)
                .Map(dest => dest.ApprovalStatus, src => src.ApprovalStatus)
                .Map(dest => dest.SoFolder, src => src.SoFolder)
                .Map(dest => dest.Remark, src => src.Remark)
                .Map(dest => dest.IsDeleted, src => src.IsDeleted)
                .Map(dest => dest.IsLocked, src => src.IsLocked)
                .Map(dest => dest.CreatedBy, src => src.CreatedBy)
                .Map(dest => dest.UpdatedBy, src => src.UpdatedBy)
                .Map(dest => dest.OrderDate, src => src.OrderDate)
                .Map(dest => dest.DeliverDate, src => src.DeliveryDate)
                .Map(dest => dest.ReplyDate, src => src.ReplyDate)
                .Map(dest => dest.ETA, src => src.ETA)
                .Map(dest => dest.ETD, src => src.ETD)
                .Map(dest => dest.CreatedDate, src => src.CreatedDate)
                .Map(dest => dest.UpdatedDate, src => src.UpdatedDate)
                // 中文注释：以下为 BaseLineModel / IDataErrorInfo 的运行时字段，不参与数据映射。
                .Ignore(dest => dest.State)
                .Ignore(dest => dest.是否程序内部修改)
                .Ignore(dest => dest.IsChecked)
                .Ignore(dest => dest.Error)
                .IgnoreNonMapped(true);

            config.NewConfig<SalesOrderHeaderModel, SalesOrderHeaderDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.OrderStatus, src => src.OrderStatus)
                .Map(dest => dest.OrderNo, src => src.OrderNo)
                .Map(dest => dest.CustomerId, src => src.CustomerId)
                .Map(dest => dest.CustomerCode, src => src.CustomerCode)
                .Map(dest => dest.CustomerPoNo, src => src.PoNo)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)
                .Map(dest => dest.Currency, src => src.Currency)
                .Map(dest => dest.ExchangeRate, src => src.ExchangeRate)
                .Map(dest => dest.TotalAmountOriginalCurrency, src => src.TotalAmountOriginalCurrency)
                .Map(dest => dest.TotalAmountLocalCurrency, src => src.TotalAmountLocalCurrency)
                .Map(dest => dest.InvoiceNo, src => src.InvoiceNo)
                .Map(dest => dest.ShipToName, src => src.ShipToName)
                .Map(dest => dest.ShipToAddress, src => src.ShipToAddress)
                .Map(dest => dest.ShipToPhone, src => src.ShipToPhone)
                .Map(dest => dest.Incoterm, src => src.Incoterm)
                .Map(dest => dest.IncotermLocation, src => src.Incotermlocation)
                .Map(dest => dest.DepositRate, src => src.DepositRate)
                .Map(dest => dest.LeadTimeDays, src => src.LeadTimeDays)
                .Map(dest => dest.PaymentMethod, src => src.PaymentMethod)
                .Map(dest => dest.VersionNo, src => src.VersionNo)
                .Map(dest => dest.ApprovalStatus, src => src.ApprovalStatus)
                .Map(dest => dest.SoFolder, src => src.SoFolder)
                .Map(dest => dest.Remark, src => src.Remark)
                .Map(dest => dest.IsDeleted, src => src.IsDeleted)
                .Map(dest => dest.IsLocked, src => src.IsLocked)
                .Map(dest => dest.CreatedBy, src => src.CreatedBy)
                .Map(dest => dest.UpdatedBy, src => src.UpdatedBy)
                .Map(dest => dest.OrderDate, src => src.OrderDate)
                .Map(dest => dest.DeliveryDate, src => src.DeliverDate)
                .Map(dest => dest.ReplyDate, src => src.ReplyDate)
                .Map(dest => dest.ETA, src => src.ETA)
                .Map(dest => dest.ETD, src => src.ETD)
                .Map(dest => dest.CreatedDate, src => src.CreatedDate)
                .Map(dest => dest.UpdatedDate, src => src.UpdatedDate)
                .IgnoreNonMapped(true);
        }

        /// <summary>
        /// 中文注释：注册销售订单行映射。
        /// 行对象两边字段名和类型并不完全一致，例如：
        /// CustomerSKU ↔ CustomerPdSku、SubNote ↔ SubRemark、nullable decimal ↔ decimal。
        /// 因此这里不建议直接反转，而是分别写清楚双向映射。
        /// </summary>
        private static void RegisterLineMapping(TypeAdapterConfig config)
        {
            config.NewConfig<SalesOrderLineDto, SalesOrderLineModel>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.LineNumber, src => src.LineNumber)
                .Map(dest => dest.SalesOrderId, src => src.SalesOrderId)
                .Map(dest => dest.OrderNo, src => src.OrderNo)
                .Map(dest => dest.Sku, src => src.Sku)
                .Map(dest => dest.CustomerSKU, src => src.CustomerSKU)
                .Map(dest => dest.DescriptionEn, src => src.DescriptionEn)
                .Map(dest => dest.MaterialType, src => src.MaterialType)
                .Map(dest => dest.ColorNameEn, src => src.ColorNameEn)
                .Map(dest => dest.FeaturesEn, src => src.FeaturesEn)
                .Map(dest => dest.CNYPrice, src => src.CNYPrice ?? 0m)
                .Map(dest => dest.USDPrice, src => src.USDPrice ?? 0m)
                .Map(dest => dest.QTY, src => src.QTY ?? 0m)
                .Map(dest => dest.CNYAmount, src => src.CNYAmount ?? 0m)
                .Map(dest => dest.USDAmount, src => src.USDAmount ?? 0m)
                .Map(dest => dest.PcsOfCarton, src => src.PcsOfCarton ?? 0m)
                .Map(dest => dest.NwOfCarton, src => src.NwOfCarton ?? 0m)
                .Map(dest => dest.GwOfCarton, src => src.GwOfCarton ?? 0m)
                .Map(dest => dest.CbmOfCarton, src => src.CbmOfCarton ?? 0m)
                .Map(dest => dest.Ctns, src => src.Ctns ?? 0m)
                .Map(dest => dest.NwSubTotal, src => src.NwSubTotal ?? 0m)
                .Map(dest => dest.GwSubTotal, src => src.GwSubTotal ?? 0m)
                .Map(dest => dest.CbmSubTotal, src => src.CbmSubTotal ?? 0m)
                .Map(dest => dest.SubNote, src => src.SubNote)
                .Ignore(dest => dest.State)
                .Ignore(dest => dest.是否程序内部修改)
                .Ignore(dest => dest.IsChecked)
                .TwoWays()
                .IgnoreNonMapped(true);

            config.NewConfig<SalesOrderLineModel, SalesOrderLineDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.LineNumber, src => src.LineNumber)
                .Map(dest => dest.SalesOrderId, src => src.SalesOrderId)
                .Map(dest => dest.OrderNo, src => src.OrderNo)
                .Map(dest => dest.Sku, src => src.Sku)
                .Map(dest => dest.CustomerSKU, src => src.CustomerSKU)
                .Map(dest => dest.DescriptionEn, src => src.DescriptionEn)
                .Map(dest => dest.MaterialType, src => src.MaterialType)
                .Map(dest => dest.ColorNameEn, src => src.ColorNameEn)
                .Map(dest => dest.FeaturesEn, src => src.FeaturesEn)
                .Map(dest => dest.CNYPrice, src => (decimal?)src.CNYPrice)
                .Map(dest => dest.USDPrice, src => (decimal?)src.USDPrice)
                .Map(dest => dest.QTY, src => (decimal?)src.QTY)
                .Map(dest => dest.CNYAmount, src => (decimal?)src.CNYAmount)
                .Map(dest => dest.USDAmount, src => (decimal?)src.USDAmount)
                .Map(dest => dest.PcsOfCarton, src => (decimal?)src.PcsOfCarton)
                .Map(dest => dest.NwOfCarton, src => (decimal?)src.NwOfCarton)
                .Map(dest => dest.GwOfCarton, src => (decimal?)src.GwOfCarton)
                .Map(dest => dest.CbmOfCarton, src => (decimal?)src.CbmOfCarton)
                .Map(dest => dest.Ctns, src => (decimal?)src.Ctns)
                .Map(dest => dest.NwSubTotal, src => (decimal?)src.NwSubTotal)
                .Map(dest => dest.GwSubTotal, src => (decimal?)src.GwSubTotal)
                .Map(dest => dest.CbmSubTotal, src => (decimal?)src.CbmSubTotal)
                .Map(dest => dest.SubNote, src => src.SubNote)
                .IgnoreNonMapped(true);
        }

    }
}
