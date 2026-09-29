using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>按采购单身份汇集来源、跟进、正式入库和业务日志的只读证据。</summary>
    public sealed class PurchaseOrderTraceDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string OrderNo { get; set; }
        public string SupplierName { get; set; }
        public int OriginKind { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public List<PurchaseOrderTraceLineDto> Lines { get; set; } = new List<PurchaseOrderTraceLineDto>();
        public List<PurchaseOrderTraceOperationDto> Operations { get; set; } = new List<PurchaseOrderTraceOperationDto>();
    }
}
