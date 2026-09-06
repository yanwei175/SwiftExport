using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>销售履约 Manager 的订单级只读查询行。</summary>
    public class SalesFulfillmentManagerListItemDto
    {
        public Guid SalesOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public int LineCount { get; set; }
        public int SourceDemandCount { get; set; }
        public int AnomalyCount { get; set; }
        public decimal DemandQty { get; set; }
        public decimal? ShippedQty { get; set; }
        public decimal? UnshippedQty { get; set; }
        public SalesOrderFulfillmentStatusEnum? FulfillmentStatus { get; set; }
    }
}
