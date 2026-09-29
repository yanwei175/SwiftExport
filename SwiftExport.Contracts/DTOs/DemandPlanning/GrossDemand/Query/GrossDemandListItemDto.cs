using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 来源需求管理页只读查询行；链路提示仅表明事实存在，不代表完成率。
    /// </summary>
    /// <remarks>来源快照来自 GrossDemand；链路提示用有效关系和正式过账事实计算。</remarks>
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
        /// <summary>是否存在活动物料拆解行。</summary>
        public bool HasActiveMaterial { get; set; }
        /// <summary>是否存在来源版本、客户、目标行与单位均可核实的活动采购 Link。</summary>
        public bool HasValidPurchase { get; set; }
        /// <summary>是否存在已过账且未反向的装箱行。</summary>
        public bool HasPostedDelivery { get; set; }

        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
