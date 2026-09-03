using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购管理页的新链专用分页筛选；空状态集合表示不限制对应状态。</summary>
    public sealed class PurchaseOrderManagerSearchRequest : PagedRequestBase
    {
        public string OrderNo { get; set; }
        public string SourceNo { get; set; }
        public string SupplierKeyword { get; set; }
        public string Sku { get; set; }
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
        public List<PurchaseOrderLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<PurchaseOrderLifecycleStatusEnum>();
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
        public DateTime? ExpectedCompletionDateFrom { get; set; }
        public DateTime? ExpectedCompletionDateTo { get; set; }
    }
}
