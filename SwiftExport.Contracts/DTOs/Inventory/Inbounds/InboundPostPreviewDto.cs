using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>过账预览快照，不构成库存或采购额度预留。</summary>
    public class InboundPostPreviewDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseName { get; set; }
        public bool CanPost { get; set; }
        public List<InboundPostPreviewLineDto> Lines { get; set; } = new List<InboundPostPreviewLineDto>();
        public List<InboundPostStockImpactDto> StockImpacts { get; set; } = new List<InboundPostStockImpactDto>();
        public List<InboundPostBlockerDto> Blockers { get; set; } = new List<InboundPostBlockerDto>();
    }
}
