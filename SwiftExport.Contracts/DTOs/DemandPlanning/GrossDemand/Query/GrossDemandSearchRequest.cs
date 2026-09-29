using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>
    /// 来源需求管理查询条件；链路条件按当前有效关系筛选，不改变来源生命周期。
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
        /// <summary>客户编码或名称关键词。</summary>
        public string CustomerKeyword { get; set; }
        /// <summary>当前链路快捷条件：All、NoMaterial、HasMaterial、NoPurchase、HasPurchase、NoDelivery、HasDelivery。</summary>
        public string TraceView { get; set; }
    }
}
