using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>取消采购草稿的幂等命令。</summary>
    public class CancelPurchaseDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
