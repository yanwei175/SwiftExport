using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Sources
{
    /// <summary>在指定来源内复核已应用查询命中的成品需求；没有分页，不改变全量子表读取范围。</summary>
    public class OrderTrackingSourceMatchRequest
    {
        /// <summary>与来源搜索相同的关键词匹配口径。</summary>
        public string Keyword { get; set; } = string.Empty;
        /// <summary>关注项集合内并集，与其他条件在同一成品需求上求交；受同等权限保护。</summary>
        public List<string> AttentionCodes { get; set; } = new List<string>();
        /// <summary>已应用的来源类型条件；省略或null由服务端默认SO，显式空集合不限。不得预填集合，避免反序列化追加默认值。</summary>
        public List<string> SourceTypes { get; set; }
        /// <summary>已应用的客户代码精确多选，空集合不限。</summary>
        public List<string> CustomerCodes { get; set; } = new List<string>();
        /// <summary>要求交期起日，含当天。</summary>
        public DateTime? RequiredDateFrom { get; set; }
        /// <summary>要求交期止日，含当天；空交期不匹配日期条件。</summary>
        public DateTime? RequiredDateTo { get; set; }
        /// <summary>生命周期集合内并集，空集合不限。</summary>
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
    }
}
