using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 成品需求事实查询条件；普通列表分页，指定来源时全量返回该来源的匹配需求。
    /// </summary>
    public class GrossDemandSearchRequest : PagedRequestBase
    {
        public List<string> SourceTypes { get; set; } = new List<string>();
        public List<string> SourceNos { get; set; } = new List<string>();
        public List<string> Skus { get; set; } = new List<string>();
        public List<string> CustomerSkus { get; set; } = new List<string>();
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
        public DateTime? RequiredDateFrom { get; set; }
        public DateTime? RequiredDateTo { get; set; }
        public bool? IsNonStandard { get; set; }
        public Guid? SourceUid { get; set; }
        /// <summary>按客户身份精确筛选；空集合表示不限制客户。</summary>
        public List<Guid> CustomerIds { get; set; } = new List<Guid>();
    }
}
