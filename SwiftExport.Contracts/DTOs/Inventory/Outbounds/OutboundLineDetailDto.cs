using System;
using SwiftExport.Contracts.DTOs.Base;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>出库行事实及当前产品、余额投影；不作为写入参数。</summary>
    public class OutboundLineDetailDto : BaseDto
    {
        public Guid OutboundDocumentId { get; set; }
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        public string SkuSnapshot { get; set; }
        public string DescriptionSnapshot { get; set; }
        public string UnitSnapshot { get; set; }
        public decimal Quantity { get; set; }
        public Guid CommandItemId { get; set; }
        public Guid? OriginalLineId { get; set; }
        public string LinkSku { get; set; }
        public string ImagePath { get; set; }
        public decimal OnHandQty { get; set; }
    }
}
