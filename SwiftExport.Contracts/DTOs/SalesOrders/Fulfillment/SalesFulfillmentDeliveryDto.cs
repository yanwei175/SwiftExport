using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>按来源需求锚定的装箱行历史；仅有效过账行计入正式交付。</summary>
    public class SalesFulfillmentDeliveryDto
    {
        /// <summary>本次查询中的销售行归属。</summary>
        public Guid SalesOrderLineId { get; set; }
        public Guid PackingListId { get; set; }
        public Guid PackingListLineId { get; set; }
        public string PackingListNo { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public decimal Qty { get; set; }
        public string UnitSnapshot { get; set; } = string.Empty;
        public bool CountsTowardDelivery { get; set; }
        public DateTime? PostedDate { get; set; }
        public DateTime? ReversedDate { get; set; }
    }
}
