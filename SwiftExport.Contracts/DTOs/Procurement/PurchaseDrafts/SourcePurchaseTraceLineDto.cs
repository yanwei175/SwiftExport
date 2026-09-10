using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <remarks>来源表：DemandExecutionLink、PurchaseOrderHead、PurchaseOrderLine。</remarks>
    public class SourcePurchaseTraceLineDto
    {
        public Guid GrossDemandId { get; set; }
        public Guid NetDemandId { get; set; }
        /// <summary>历史计划分配 Uid；新采购 Link 不再写入。</summary>
        public Guid? AllocationId { get; set; }
        public Guid DemandExecutionLinkId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public int? SourceLineNumber { get; set; }
        public string Sku { get; set; }
        public decimal NetDemandQty { get; set; }
        public decimal LinkQty { get; set; }
        public decimal OriginalLinkQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderNo { get; set; }
        public PurchaseOrderLifecycleStatusEnum? PurchaseLifecycleStatus { get; set; }
        public ConfirmationStatusEnum PurchaseConfirmationStatus { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int? PurchaseOrderLineNumber { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
    }
}

