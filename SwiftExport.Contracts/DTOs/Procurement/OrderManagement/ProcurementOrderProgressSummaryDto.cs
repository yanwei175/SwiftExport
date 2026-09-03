using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购详情所需的最新供应商跟进摘要；不包含历史集合，也不允许详情追加反馈。</summary>
    public class ProcurementOrderProgressSummaryDto
    {
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        public decimal ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime ReportedDate { get; set; }
        public string ReportedBy { get; set; }
    }
}
