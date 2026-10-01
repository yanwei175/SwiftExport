using System;
using System.Collections.Generic;
using System.Linq;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Services
{
    /// <summary>组装导入检查和新建草稿请求；明确区分原表型号、系统身份及服务器生成的单头身份。</summary>
    internal static class SalesOrderImportRequests
    {
        /// <summary>复制当次检查输入；非法数字必须由调用者先阻断。</summary>
        internal static CheckSalesOrderImportRequest Check(SalesOrderImportHeader h, IEnumerable<SalesOrderImportLine> lines, bool acceptExisting)
        {
            return new CheckSalesOrderImportRequest {
                CustomerCode = h.CustomerCode, AcceptExistingOrders = acceptExisting,
                Header = new SalesOrderImportHeaderInput { InvoiceNo = h.InvoiceNo, CustomerPoNo = h.CustomerPoNo,
                    OrderDate = h.OrderDate, Currency = h.Currency, ExchangeRate = h.ExchangeRate, DepositRate = h.DepositRate },
                Lines = lines.Select(l => new SalesOrderImportLineInput { ClientLineId = l.Uid,
                    CustomerSku = l.CustomerSku, SelectedCustomerProductId = l.SelectedMatchId ?? l.CustomerProductId,
                    LinkSku = l.OriginalLinkSku, IsNonStandard = l.IsNonStandard, DescriptionEn = l.DescriptionEn,
                    Unit = l.UnitSnapshot, Qty = l.Qty, CnyPrice = l.CnyPrice, UsdPrice = l.UsdPrice,
                    PcsOfCarton = l.PcsOfCarton, GwOfCarton = l.GwOfCarton, NwOfCarton = l.NwOfCarton,
                    CbmOfCarton = l.CbmOfCarton, AcceptLinkSkuMismatch = l.AcceptLinkSkuMismatch }).ToList()
            };
        }
        /// <summary>新建头必须使用空 Uid，明细保持本地稳定 Uid；不传原表 UpdateDate 或计算汇总。</summary>
        internal static SaveSalesOrderRequest Save(SalesOrderImportHeader h, IEnumerable<SalesOrderImportLine> lines)
        {
            return new SaveSalesOrderRequest { CommandId = Guid.NewGuid(),
                Header = new SalesOrderEditHeaderDto { Uid = Guid.Empty, Version = 0,
                    CustomerId = h.CustomerId, CustomerCode = h.CustomerCode, OrderNo = "",
                    InvoiceNo = h.InvoiceNo, CustomerPoNo = h.CustomerPoNo, Currency = h.Currency,
                    ExchangeRate = h.ExchangeRate.Value, DepositRate = h.DepositRate, LeadTimeDays = h.LeadTimeDays,
                    PaymentMethod = h.PaymentMethod, ShippedBy = h.ShippedBy, Incoterm = h.Incoterm,
                    IncotermSortRemark = h.IncotermSortRemark, IncotermLocation = h.IncotermLocation,
                    ShipToName = h.ShipToName, ShipToAddress = h.ShipToAddress, ShipToPhone = h.ShipToPhone,
                    Remark = h.Remark, OrderDate = h.OrderDate, DeliveryDate = h.DeliveryDate,
                    ReplyDate = h.ReplyDate, Etd = h.Etd, Eta = h.Eta },
                Lines = lines.Select((l, i) => new SalesOrderEditLineDto { Uid = l.Uid, Version = 0, LineNumber = i + 1,
                    IsNonStandard = l.IsNonStandard, ProductId = l.IsNonStandard ? null : l.ProductId,
                    CustomerProductId = l.IsNonStandard ? null : l.CustomerProductId,
                    Sku = l.Sku, LinkSku = l.IsNonStandard ? l.OriginalLinkSku : l.LinkSku, CustomerSku = l.CustomerSku,
                    DescriptionEn = l.DescriptionEn, ColorNameEn = l.ColorNameEn, FeaturesEn = l.FeaturesEn,
                    Qty = l.Qty.Value, UnitSnapshot = l.UnitSnapshot, CnyPrice = l.CnyPrice, UsdPrice = l.UsdPrice,
                    PcsOfCarton = l.PcsOfCarton, GwOfCarton = l.GwOfCarton, NwOfCarton = l.NwOfCarton,
                    CbmOfCarton = l.CbmOfCarton, ExpectedDeliveryDate = l.ExpectedDeliveryDate, SubNote = l.SubNote }).ToList(),
                DeletedLines = new List<SalesOrderDeletedLineDto>() };
        }
    }
}
