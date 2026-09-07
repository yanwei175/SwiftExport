using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery
{
    /// <summary>来源需求的统一可交付投影；正式已发仅统计锚定本来源需求的有效客户出库。</summary>
    public class SourceDemandDeliveryItemDto
    {
        public Guid GrossDemandId { get; set; }
        public int DemandVersion { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceVersion { get; set; }
        public Guid? CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public Guid? ProductId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal DemandQty { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal RemainingQty { get; set; }
        public bool IsActive { get; set; }
        public DateTime? RequiredDate { get; set; }
        public Guid? OrderPoolSalesOrderId { get; set; }
        public bool IsInventoryActionAllowed { get; set; }
        public string InventoryBlockerCode { get; set; }
    }
}
