using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto
{
    /// <summary>更新待确认采购草稿的头部商务白名单和全部既有行。</summary>
    public sealed class UpdatePurchaseOrderDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Remark { get; set; }
        public string PackagingRequirements { get; set; }
        public string DiscountRemark { get; set; }
        public List<UpdatePurchaseOrderDraftLineCommand> Lines { get; set; } = new List<UpdatePurchaseOrderDraftLineCommand>();
    }
}
