using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商反馈录入的选单结果；提供订单识别、行数和登记资格，不汇总采购进度。</summary>
    public sealed class PurchaseOrderProgressListItemDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public string OrderNo { get; set; }
        public DateTime? OrderDate { get; set; }
        public string BoNo { get; set; }
        public string SupplierName { get; set; }
        public int LineCount { get; set; }
        public string DemandSourceNo { get; set; }
        public bool CanReportProgress { get; set; }
        public string ReadOnlyReason { get; set; }
    }
}
