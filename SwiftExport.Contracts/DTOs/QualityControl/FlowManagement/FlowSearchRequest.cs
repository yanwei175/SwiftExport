using System;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>按物料身份、历史 SKU 和测试日期范围分页查询流量测试组。</summary>
    public sealed class FlowSearchRequest : PagedRequestBase
    {
        public Guid? MaterialId { get; set; }
        public string SkuSnapshot { get; set; }
        public DateTime? TestDateFrom { get; set; }
        public DateTime? TestDateTo { get; set; }
    }
}
