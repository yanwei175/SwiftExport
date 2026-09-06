using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>一个销售订单的只读履约详情及其采购关联。</summary>
    public class SalesFulfillmentDetailDto
    {
        public Guid SalesOrderId { get; set; }
        public int SalesOrderVersion { get; set; }
        public string OrderNo { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public int? LatestSourceVersion { get; set; }
        public List<SalesFulfillmentDetailLineDto> Lines { get; set; } = new List<SalesFulfillmentDetailLineDto>();
        public List<SalesFulfillmentPurchaseDto> LinePurchases { get; set; } = new List<SalesFulfillmentPurchaseDto>();
        public List<SalesFulfillmentPurchaseDto> OrderPurchases { get; set; } = new List<SalesFulfillmentPurchaseDto>();
    }
}
