using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>销售履约 Detail 中的销售行与来源需求事实。</summary>
    public class SalesFulfillmentDetailLineDto
    {
        public Guid SalesOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public Guid? SourceDemandId { get; set; }
        public int? SourceVersion { get; set; }
        public GrossDemandLifecycleStatusEnum? SourceLifecycleStatus { get; set; }
        public decimal? ShippedQty { get; set; }
        public decimal? UnshippedQty { get; set; }
        public string AnomalyCode { get; set; }
        public string AnomalyMessage { get; set; }
    }
}
