using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>采购入库校验专用来源投影，不作为客户端写入参数。</summary>
    public class InboundPurchasePostingSourceDto
    {
        public Guid SourceLineId { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid ProductId { get; set; }
        public string SourceNo { get; set; }
        public string Unit { get; set; }
        public string CurrentUnit { get; set; }
        public bool IsReceivable { get; set; }
        public bool IsCustomerSupplied { get; set; }
        public decimal OriginalQty { get; set; }
        public decimal PostedNetQty { get; set; }
    }
}
