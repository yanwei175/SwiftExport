using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 毛需求工作台只读查询行；不包含选择、脏状态等客户端状态。
    /// </summary>
    /// <remarks>来源表：GrossDemand、Product、SalesOrderHeader、SalesOrderLine、NetDemand。</remarks>
    public class GrossDemandListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        public GrossDemandDecompositionStatusEnum DecompositionStatus { get; set; }
        public int DecompositionRevision { get; set; }

        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }

        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }

        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public string Remark { get; set; }

        public int ActiveNetDemandCount { get; set; }
        public int CustomerSuppliedNetDemandCount { get; set; }
        public DemandExecutionProgressEnum ExecutionProgress { get; set; }
        public bool CanDecompose { get; set; }
        public bool CanRollback { get; set; }
        public string RollbackBlockerSummary { get; set; }

        public DateTime? DecomposedAt { get; set; }
        public string DecomposedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
