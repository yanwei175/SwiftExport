using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>手工出库整单反过账原流水与库存恢复预览。</summary>
    public class OutboundReverseImpactDto
    {
        public Guid LineId { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public decimal OriginalQuantityDelta { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal RestoreQuantity { get; set; }
        public decimal ExpectedQuantity { get; set; }
    }
}

