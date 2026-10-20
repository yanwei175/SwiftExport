using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>当前占用物料需求的草稿引用，仅向具备采购读取权限的调用者返回。</summary>
    public sealed class PurchaseDraftReferenceDto
    {
        /// <summary>采购单稳定身份，用于打开已有草稿。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>采购单号，用于说明占用来源。</summary>
        public string OrderNo { get; set; }
    }
}
