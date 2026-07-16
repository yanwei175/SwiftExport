using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 毛需求详情及来源快照。
    /// </summary>
    public class GrossDemandDetailDto
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

        public DateTime? DecomposedAt { get; set; }
        public string DecomposedBy { get; set; }
        public DateTime? LifecycleChangedAt { get; set; }
        public string LifecycleChangedBy { get; set; }
        public string LifecycleChangedReason { get; set; }

        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
