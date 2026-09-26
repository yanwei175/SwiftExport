using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting
{
    /// <summary>同仓同产品合并后的库存影响，仅供提交前参考。</summary>
    public class InboundPostStockImpactDto
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal QuantityDelta { get; set; }
        public decimal ExpectedQuantity { get; set; }
    }
}
