using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal
{
    /// <summary>手工入库整单反过账预览版本请求。</summary>
    public class InboundReversePreviewRequest
    {
        public int ExpectedVersion { get; set; }
    }
}
