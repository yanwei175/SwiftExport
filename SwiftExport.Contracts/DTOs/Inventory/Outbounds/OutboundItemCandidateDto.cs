using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>仓库余额选项；数量只作选择参考，过账时重新检查。</summary>
    public class OutboundItemCandidateDto
    {
        public Guid Uid { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string Description { get; set; }
        public string ImagePath { get; set; }
        public string Unit { get; set; }
        public decimal OnHandQty { get; set; }
    }
}
