using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal
{
    /// <summary>人工入库整单撤销预览；汇总扣减量，不预占余额。</summary>
    public class InboundReversePreviewDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public string WarehouseName { get; set; }
        public bool CanReverse { get; set; }
        public List<InboundReverseLedgerDto> Ledgers { get; set; } = new List<InboundReverseLedgerDto>();
        public List<InboundReverseImpactDto> StockImpacts { get; set; } = new List<InboundReverseImpactDto>();
        public List<InboundPreviewBlockerDto> Blockers { get; set; } = new List<InboundPreviewBlockerDto>();
    }
}
