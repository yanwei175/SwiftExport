using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Sources
{
    /// <summary>来源组分页条件；筛选匹配完整需求集合后按稳定来源身份分组。</summary>
    public class OrderTrackingSourceSearchRequest : PagedRequestBase
    {
        /// <summary>来源默认SO；空集合不限SO/CR/MD。</summary>
        public List<string> SourceTypes { get; set; } = new List<string> { "SO" };
        /// <summary>当前客户代码精确多选，空集合不限。</summary>
        public List<string> CustomerCodes { get; set; } = new List<string>();
        /// <summary>要求交期起日，含当天。</summary>
        public DateTime? RequiredDateFrom { get; set; }
        /// <summary>要求交期止日，含当天；空交期不匹配。</summary>
        public DateTime? RequiredDateTo { get; set; }
        /// <summary>生命周期集合内并集，空集合不限。</summary>
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
    }
}
