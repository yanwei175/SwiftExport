using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购预览警告覆盖的物料需求辨认摘要，用于显示确认范围而不承载页面状态。</summary>
    public class PurchaseDraftWarningAffectedObjectDto
    {
        /// <summary>受影响物料需求的稳定身份。</summary>
        public Guid NetDemandId { get; set; }

        /// <summary>来源单号快照。</summary>
        public string SourceNo { get; set; } = string.Empty;

        /// <summary>来源行号；来源没有行号时为空。</summary>
        public int? SourceLineNumber { get; set; }

        /// <summary>物料 SKU 快照。</summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>产品关联型号快照。</summary>
        public string LinkSku { get; set; } = string.Empty;

        /// <summary>该物料需求是否包含在本次采购预览负载中。</summary>
        public bool IsIncludedInCurrentPurchase { get; set; }

        /// <summary>应用本次选择后仍未转采购的数量。</summary>
        public decimal RemainingAfterSelectionQty { get; set; }
    }
}
