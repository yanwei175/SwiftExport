using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商反馈追加结果；幂等重放返回同一报告标识。</summary>
    public sealed class PurchaseOrderProgressReportResult
    {
        public ProcurementCommandOutcomeEnum Outcome { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public Guid SupplierProgressReportId { get; set; }
        public string Message { get; set; }
    }
}
