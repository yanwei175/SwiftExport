using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>创建有客户归属、实际供应商和明确收货目标的独立采购；不接受需求来源。</summary>
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
