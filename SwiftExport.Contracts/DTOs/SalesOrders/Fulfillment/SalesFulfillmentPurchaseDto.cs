using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>销售履约 Detail 中一条行级组件采购或订单级采购关联。</summary>
    public class SalesFulfillmentPurchaseDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderNo { get; set; }
        public Guid? PurchaseOrderLineId { get; set; }
        public int? PurchaseOrderLineNumber { get; set; }
        public Guid? SalesOrderLineId { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public decimal? AllocatedQty { get; set; }
        public decimal? ReceivedQty { get; set; }
        public decimal? LatestReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime? LatestFollowUpDate { get; set; }
        public bool IsOrderLevelOnly { get; set; }
    }
}
