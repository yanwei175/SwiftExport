using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商反馈录入所需的采购行事实及反馈历史；正式收货由订单跟踪独立读取。</summary>
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
        public DateTime? PlannedDeliveryDate { get; set; }
        public SupplierProgressReportDto LatestProgress { get; set; }
        public List<SupplierProgressReportDto> ProgressHistory { get; set; } = new List<SupplierProgressReportDto>();
    }
}
