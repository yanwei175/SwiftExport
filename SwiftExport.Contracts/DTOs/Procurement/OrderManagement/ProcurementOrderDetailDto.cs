using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>新链订单自身身份、商务快照与行；不组合旧列表、跟进或追溯模型。</summary>
    public sealed class ProcurementOrderDetailDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string OrderNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string Currency { get; set; }
        public DateTime OrderDate { get; set; }
        public string Remark { get; set; }
        public string PackagingRequirements { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public Guid? ActiveConfirmationId { get; set; }
        public List<ProcurementOrderLineDto> Lines { get; set; } = new List<ProcurementOrderLineDto>();
    }
}
