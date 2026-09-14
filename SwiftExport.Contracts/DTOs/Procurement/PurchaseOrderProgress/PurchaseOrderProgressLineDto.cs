using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>采购跟进行摘要；收货数量来自采购行身份聚合，反馈历史按 Sequence 降序返回。</summary>
    public sealed class PurchaseOrderProgressLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public string MainImage { get; set; }
        public string Sku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public decimal Quantity { get; set; }
        public decimal ReceivedQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public SupplierProgressReportDto LatestProgress { get; set; }
        public List<SupplierProgressReportDto> ProgressHistory { get; set; } = new List<SupplierProgressReportDto>();
    }
}
