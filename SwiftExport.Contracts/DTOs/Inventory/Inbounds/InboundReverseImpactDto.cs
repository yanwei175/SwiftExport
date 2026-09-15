using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>手工入库整单反过账原流水与库存扣减预览。</summary>
    public class InboundReverseImpactDto
    {
        public Guid WarehouseSiteId { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public decimal OriginalQuantityDelta { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal ReverseQuantity { get; set; }
        public decimal ExpectedQuantity { get; set; }
    }
}
