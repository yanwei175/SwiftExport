using System;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal
{
    /// <summary>已核对的原入库流水身份及数量，只用于只读预览。</summary>
    public class InboundReverseLedgerDto
    {
        public Guid LedgerId { get; set; }
        public Guid LineId { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
    }
}
