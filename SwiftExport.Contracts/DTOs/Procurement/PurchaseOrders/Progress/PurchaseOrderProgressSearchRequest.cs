using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商反馈录入的选单筛选；空状态集合表示不限制对应订单状态。</summary>
    public sealed class PurchaseOrderProgressSearchRequest : PagedRequestBase
    {
        public string OrderNo { get; set; }
        public string SupplierKeyword { get; set; }
        public string Sku { get; set; }
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
    }
}
