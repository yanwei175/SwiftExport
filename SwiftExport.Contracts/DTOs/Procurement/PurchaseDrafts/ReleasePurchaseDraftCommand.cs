using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>按有效预览执行采购草稿减量的幂等命令。</summary>
    public class ReleasePurchaseDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal NewTotalQty { get; set; }
        public string Reason { get; set; }
        public string PreviewToken { get; set; }
    }
}
