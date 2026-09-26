using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>采购跟进列表筛选；空状态集合表示不限制对应维度。</summary>
    public sealed class PurchaseOrderProgressSearchRequest : PagedRequestBase
    {
        public string OrderNo { get; set; }
        public string SupplierKeyword { get; set; }
        public string Sku { get; set; }
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        public List<SupplierArrangementStatusEnum> ArrangementStatuses { get; set; } = new List<SupplierArrangementStatusEnum>();
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
        public DateTime? ExpectedReadyDateFrom { get; set; }
        public DateTime? ExpectedReadyDateTo { get; set; }
    }
}
