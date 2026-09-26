using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>手工出库整单反过账预览版本请求。</summary>
    public class OutboundReversePreviewRequest
    {
        public int ExpectedVersion { get; set; }
    }
}

