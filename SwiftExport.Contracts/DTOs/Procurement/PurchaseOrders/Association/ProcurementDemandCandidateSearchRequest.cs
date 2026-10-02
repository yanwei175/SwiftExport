using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>按用户明确选择的采购行检索物料候选；来源筛选可选，不自动分配。</summary>
    public sealed class ProcurementDemandCandidateSearchRequest
    {
        public Guid PurchaseOrderLineId { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public string Keyword { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 30;
    }
}
