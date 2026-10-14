using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery
{
    /// <summary>成品需求可交付列表查询条件；查询具有客户和正剩余交付量的活动需求。</summary>
    public class SourceDemandDeliverySearchRequest : PagedRequestBase
    {
        /// <summary>可选来源类型精确筛选。</summary>
        public string SourceType { get; set; }
        /// <summary>可选最终客户筛选。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>可选来源单号文本筛选。</summary>
        public string SourceNo { get; set; }
        /// <summary>可选 SKU 文本筛选。</summary>
        public string Sku { get; set; }
    }
}
