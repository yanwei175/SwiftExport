using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿减量预览结果。</summary>
    public class PurchaseDraftReleasePreviewResponse
    {
        public Guid PurchaseOrderId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal CurrentTotalQty { get; set; }
        public decimal NewTotalQty { get; set; }
        public decimal ReleaseQty { get; set; }
        public string PreviewToken { get; set; }
        public bool HasBlockers { get; set; }
        public List<string> Blockers { get; set; } = new List<string>();
        public List<PurchaseDraftLinkReleaseDto> Releases { get; set; } = new List<PurchaseDraftLinkReleaseDto>();
    }
}
