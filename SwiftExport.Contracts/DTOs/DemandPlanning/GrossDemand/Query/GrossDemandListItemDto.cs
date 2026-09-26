using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 来源需求管理页只读查询行；不包含下游执行进度或客户端状态。
    /// </summary>
    /// <remarks>来源表：GrossDemand；不联表投影物料建立或执行状态。</remarks>
    public class GrossDemandListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }

        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceVersion { get; set; }
        public Guid? CustomerId { get; set; }

        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }
        public string Unit { get; set; }

        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public string Remark { get; set; }

        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
