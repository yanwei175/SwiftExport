using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览中的目标单据及预览行。</summary>
    public class PurchaseDraftPreviewDocumentDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid? CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string Currency { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
        public int SourceLineCount { get; set; }
        public int PreviewLineCount { get; set; }
        public decimal TotalQty { get; set; }
        public DateTime? EarliestPlannedDeliveryDate { get; set; }
        /// <summary>由后端统一构造并纳入预览一致性校验的只读单头。</summary>
        public PurchaseDraftPreviewHeaderDto Header { get; set; } = new PurchaseDraftPreviewHeaderDto();
        public List<PurchaseDraftPreviewLineDto> Lines { get; set; } = new List<PurchaseDraftPreviewLineDto>();
        public List<SafePurchaseDraftCandidateDto> SafeAppendCandidates { get; set; } = new List<SafePurchaseDraftCandidateDto>();
    }
}
