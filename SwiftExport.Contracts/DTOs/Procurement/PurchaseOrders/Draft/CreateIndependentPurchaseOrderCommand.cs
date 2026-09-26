using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>创建有客户归属和实际供应商的独立采购；不接受需求来源或实际收货仓。</summary>
    public sealed class CreateIndependentPurchaseOrderCommand
    {
        public Guid CommandId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid SupplierId { get; set; }
        public DateTime OrderDate { get; set; }
        public string Remark { get; set; }
        public string PackagingRequirements { get; set; }
        public List<CreateIndependentPurchaseOrderLineCommand> Lines { get; set; } = new List<CreateIndependentPurchaseOrderLineCommand>();
    }
}
