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
                .Map(dest => dest.订单状态, src => src.OrderStatus)
                .Map(dest => dest.单号, src => src.OrderNo)
                .Map(dest => dest.客户Id, src => src.CustomerId)
                .Map(dest => dest.客户代码, src => src.CustomerCode)
                .Map(dest => dest.PoNo, src => src.CustomerPoNo)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)
                .Map(dest => dest.货币, src => src.Currency)
                .Map(dest => dest.汇率, src => src.ExchangeRate)
                .Map(dest => dest.原币金额总计, src => src.TotalAmountOriginalCurrency)
                .Map(dest => dest.本币金额总计, src => src.TotalAmountLocalCurrency)
                .Map(dest => dest.InvoiceNo, src => src.InvoiceNo)
                .Map(dest => dest.ShipToName, src => src.ShipToName)
                .Map(dest => dest.ShipToAddress, src => src.ShipToAddress)
                .Map(dest => dest.ShipToPhone, src => src.ShipToPhone)
                .Map(dest => dest.IncotermSortRemark, src => src.IncotermSortRemark)
                .Map(dest => dest.贸易条款, src => src.Incoterm)
                .Map(dest => dest.条款地点, src => src.IncotermLocation)
                .Map(dest => dest.定金比例, src => src.DepositRate)
                .Map(dest => dest.交货天数, src => src.LeadTimeDays)
                .Map(dest => dest.支付方式, src => src.PaymentMethod)
                .Map(dest => dest.版本号, src => src.VersionNo)
                .Map(dest => dest.审批状态, src => src.ApprovalStatus)
                .Map(dest => dest.订单文件夹, src => src.SoFolder)
                .Map(dest => dest.订单备注, src => src.Remark)
                .Map(dest => dest.IsDeleted, src => src.IsDeleted)
                .Map(dest => dest.IsLocked, src => src.IsLocked)
                .Map(dest => dest.CreatedBy, src => src.CreatedBy)
                .Map(dest => dest.UpdatedBy, src => src.UpdatedBy)
                .Map(dest => dest.OrderDate, src => src.OrderDate)
                .Map(dest => dest.交货日期, src => src.DeliveryDate)
                .Map(dest => dest.回复交期, src => src.ReplyDate)
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
                .Map(dest => dest.OrderStatus, src => src.订单状态)
                .Map(dest => dest.OrderNo, src => src.单号)
                .Map(dest => dest.CustomerId, src => src.客户Id)
                .Map(dest => dest.CustomerCode, src => src.客户代码)
                .Map(dest => dest.CustomerPoNo, src => src.PoNo)
                .Map(dest => dest.ShippedBy, src => src.ShippedBy)
                .Map(dest => dest.Currency, src => src.货币)
                .Map(dest => dest.ExchangeRate, src => src.汇率)
                .Map(dest => dest.TotalAmountOriginalCurrency, src => src.原币金额总计)
                .Map(dest => dest.TotalAmountLocalCurrency, src => src.本币金额总计)
                .Map(dest => dest.InvoiceNo, src => src.InvoiceNo)
                .Map(dest => dest.ShipToName, src => src.ShipToName)
                .Map(dest => dest.ShipToAddress, src => src.ShipToAddress)
                .Map(dest => dest.ShipToPhone, src => src.ShipToPhone)
                .Map(dest => dest.Incoterm, src => src.贸易条款)
                .Map(dest => dest.IncotermLocation, src => src.条款地点)
                .Map(dest => dest.DepositRate, src => src.定金比例)
                .Map(dest => dest.LeadTimeDays, src => src.交货天数)
                .Map(dest => dest.PaymentMethod, src => src.支付方式)
                .Map(dest => dest.VersionNo, src => src.版本号)
                .Map(dest => dest.ApprovalStatus, src => src.审批状态)
                .Map(dest => dest.SoFolder, src => src.订单文件夹)
                .Map(dest => dest.Remark, src => src.订单备注)
                .Map(dest => dest.IsDeleted, src => src.IsDeleted)
                .Map(dest => dest.IsLocked, src => src.IsLocked)
                .Map(dest => dest.CreatedBy, src => src.CreatedBy)
                .Map(dest => dest.UpdatedBy, src => src.UpdatedBy)
                .Map(dest => dest.OrderDate, src => src.OrderDate)
                .Map(dest => dest.DeliveryDate, src => src.交货日期)
                .Map(dest => dest.ReplyDate, src => src.回复交期)
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
                .Map(dest => dest.行号, src => src.LineNumber)
                .Map(dest => dest.单号, src => src.OrderNo)
                .Map(dest => dest.物料编码, src => src.Sku)
                .Map(dest => dest.客户型号, src => src.CustomerSKU)
                .Map(dest => dest.描述, src => src.CustomerPdDescription)
                .Map(dest => dest.颜色, src => src.CustomerPdFinish)
                .Map(dest => dest.配置, src => src.CustomerPdFeatures)
                .Map(dest => dest.人民币单价, src => src.RMBPrice ?? 0m)
                .Map(dest => dest.美金单价, src => src.USDPrice ?? 0m)
                .Map(dest => dest.数量, src => src.QTY ?? 0m)
                .Map(dest => dest.人民币小计, src => src.RMBAmount ?? 0m)
                .Map(dest => dest.美金小计, src => src.USDAmount ?? 0m)
                .Map(dest => dest.每箱数量, src => src.PcsOfCarton ?? 0m)
                .Map(dest => dest.每箱净重, src => src.NwOfCarton ?? 0m)
                .Map(dest => dest.每箱毛重, src => src.GwOfCarton ?? 0m)
                .Map(dest => dest.每箱体积, src => src.CbmOfCarton ?? 0m)
                .Map(dest => dest.箱数, src => src.Ctns ?? 0m)
                .Map(dest => dest.净重小计, src => src.NwSubTotal ?? 0m)
                .Map(dest => dest.毛重小计, src => src.GwSubTotal ?? 0m)
                .Map(dest => dest.体积小计, src => src.CbmSubTotal ?? 0m)
                .Map(dest => dest.备注, src => src.SubNote)
                .Ignore(dest => dest.State)
                .Ignore(dest => dest.是否程序内部修改)
                .Ignore(dest => dest.IsChecked)
                .TwoWays()
                .IgnoreNonMapped(true);

            config.NewConfig<SalesOrderLineModel, SalesOrderLineDto>()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.LineNumber, src => src.行号)
                .Map(dest => dest.OrderNo, src => src.单号)
                .Map(dest => dest.Sku, src => src.物料编码)
                .Map(dest => dest.CustomerSKU, src => src.客户型号)
                .Map(dest => dest.CustomerPdDescription, src => src.描述)
                .Map(dest => dest.CustomerPdFinish, src => src.颜色)
                .Map(dest => dest.CustomerPdFeatures, src => src.配置)
                .Map(dest => dest.RMBPrice, src => (decimal?)src.人民币单价)
                .Map(dest => dest.USDPrice, src => (decimal?)src.美金单价)
                .Map(dest => dest.QTY, src => (decimal?)src.数量)
                .Map(dest => dest.RMBAmount, src => (decimal?)src.人民币小计)
                .Map(dest => dest.USDAmount, src => (decimal?)src.美金小计)
                .Map(dest => dest.PcsOfCarton, src => (decimal?)src.每箱数量)
                .Map(dest => dest.NwOfCarton, src => (decimal?)src.每箱净重)
                .Map(dest => dest.GwOfCarton, src => (decimal?)src.每箱毛重)
                .Map(dest => dest.CbmOfCarton, src => (decimal?)src.每箱体积)
                .Map(dest => dest.Ctns, src => (decimal?)src.箱数)
                .Map(dest => dest.NwSubTotal, src => (decimal?)src.净重小计)
                .Map(dest => dest.GwSubTotal, src => (decimal?)src.毛重小计)
                .Map(dest => dest.CbmSubTotal, src => (decimal?)src.体积小计)
                .Map(dest => dest.SubNote, src => src.备注)
                .IgnoreNonMapped(true);
        }

    }
}
