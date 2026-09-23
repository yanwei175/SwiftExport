using System;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources
{
    /// <summary>入库来源查询快照；当前仅采购行，数量不构成预占或过账承诺。</summary>
    public class InboundSourceCandidateDto
    {
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public Guid SourceLineId { get; set; }
        public int SourceVersion { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid ProductId { get; set; }
        public string SourceNo { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string BoNo { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string LinkSku { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public decimal OriginalQty { get; set; }
        public decimal RemainingReceivableQty { get; set; }
        /// <summary>同来源其他活动草稿数，仅提示，不预占。</summary>
        public int OtherDraftCount { get; set; }
        /// <summary>其他活动草稿录入量合计，不扣减剩余可收量。</summary>
        public decimal OtherDraftQuantity { get; set; }
    }
}
