using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery
{
    /// <summary>来源需求可交付列表查询条件；只查询销售和投诉补货认可的活动实物需求。</summary>
    public class SourceDemandDeliverySearchRequest : PagedRequestBase
    {
        public string SourceType { get; set; }
        public Guid? CustomerId { get; set; }
        public string SourceNo { get; set; }
        public string Sku { get; set; }
    }
}
