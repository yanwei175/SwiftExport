using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿减量预览请求。</summary>
    public class PurchaseDraftReleasePreviewRequest
    {
        public int ExpectedVersion { get; set; }
        public decimal NewTotalQty { get; set; }
        public string Reason { get; set; }
    }
}
