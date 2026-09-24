using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery
{
    /// <summary>来源需求的统一可交付投影；正式已发仅统计有效且已过账的 PackingList 行。</summary>
    public class SourceDemandDeliveryItemDto
    {
        /// <summary>GrossDemand 稳定标识。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>需求并发版本。</summary>
        public int DemandVersion { get; set; }
        /// <summary>来源类型快照。</summary>
        public string SourceType { get; set; }
        /// <summary>来源单据头标识。</summary>
        public Guid SourceUid { get; set; }
        /// <summary>来源单号快照。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源行标识。</summary>
        public Guid SourceLineId { get; set; }
        /// <summary>来源行号快照。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>来源事实版本。</summary>
        public int? SourceVersion { get; set; }
        /// <summary>最终交付客户标识；无法证明时为空。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>客户编码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>客户名称。</summary>
        public string CustomerName { get; set; }
        /// <summary>标准产品标识；非标来源为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>是否为不产生库存事实的非标来源。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>SKU 快照。</summary>
        public string Sku { get; set; }
        /// <summary>关联 SKU 快照。</summary>
        public string LinkSku { get; set; }
        /// <summary>客户型号快照。</summary>
        public string CustomerSku { get; set; }
        /// <summary>描述快照。</summary>
        public string Description { get; set; }
        /// <summary>颜色快照。</summary>
        public string Color { get; set; }
        /// <summary>规格快照。</summary>
        public string Specification { get; set; }
        /// <summary>来源图片路径快照。</summary>
        public string ImagePath { get; set; }
        /// <summary>交付单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>来源需求数量。</summary>
        public decimal DemandQty { get; set; }
        /// <summary>有效已过账 PackingList 行的交付净额。</summary>
        public decimal DeliveredQty { get; set; }
        /// <summary>需求数量减交付净额。</summary>
        public decimal RemainingQty { get; set; }
        /// <summary>GrossDemand 当前生命周期。</summary>
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>当前是否允许继续交付。</summary>
        public bool IsActive { get; set; }
        /// <summary>来源要求交期。</summary>
        public DateTime? RequiredDate { get; set; }
    }
}
